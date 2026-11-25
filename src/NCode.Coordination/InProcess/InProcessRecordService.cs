// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The records of the in-process provider.
/// </summary>
/// <param name="table">The record table, shared with the transaction service.</param>
/// <remarks>
/// Every operation runs entirely while holding the table's lock, so it is linearizable in the process.
/// </remarks>
internal class InProcessRecordService(InProcessRecordTable table) : IRecordService
{
    /// <inheritdoc />
    public ValueTask<RecordWriteResult> CreateAsync(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lifetime);
        InProcessGuard.CheckValue(value, nameof(value));
        InProcessGuard.Check(options, cancellationToken);

        lock (table.Gate)
        {
            if (lifetime.Session is not null)
            {
                return ValueTask.FromResult(RecordWriteResult.SessionEnded);
            }

            if (table.Find(key) is not null)
            {
                return ValueTask.FromResult(RecordWriteResult.Conflict);
            }

            return ValueTask.FromResult(Write(key, value, lifetime));
        }
    }

    /// <inheritdoc />
    public ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        lock (table.Gate)
        {
            return ValueTask.FromResult(table.Find(key));
        }
    }

    /// <inheritdoc />
    public ValueTask<RecordWriteResult> CompareAndSwapAsync(
        CoordinationKey key,
        Revision expected,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(lifetime);
        InProcessGuard.CheckValue(value, nameof(value));
        InProcessGuard.Check(options, cancellationToken);

        lock (table.Gate)
        {
            if (lifetime.Session is not null)
            {
                return ValueTask.FromResult(RecordWriteResult.SessionEnded);
            }

            var current = table.Find(key);
            if (current is null)
            {
                return ValueTask.FromResult(RecordWriteResult.NotFound);
            }

            if (current.Revision != expected)
            {
                return ValueTask.FromResult(RecordWriteResult.Conflict);
            }

            return ValueTask.FromResult(Write(key, value, lifetime));
        }
    }

    /// <inheritdoc />
    public ValueTask<RecordDeleteStatus> CompareAndDeleteAsync(
        CoordinationKey key,
        Revision expected,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(expected);
        InProcessGuard.Check(options, cancellationToken);

        lock (table.Gate)
        {
            var current = table.Find(key);
            if (current is null)
            {
                return ValueTask.FromResult(RecordDeleteStatus.NotFound);
            }

            if (current.Revision != expected)
            {
                return ValueTask.FromResult(RecordDeleteStatus.Conflict);
            }

            table.Remove(key);
            table.Advance(key.Keyspace);
            return ValueTask.FromResult(RecordDeleteStatus.Deleted);
        }
    }

    /// <inheritdoc />
    public ValueTask<StoredRecord?> TakeAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        lock (table.Gate)
        {
            var current = table.Find(key);
            if (current is null)
            {
                return ValueTask.FromResult<StoredRecord?>(null);
            }

            table.Remove(key);
            table.Advance(key.Keyspace);
            return ValueTask.FromResult<StoredRecord?>(current);
        }
    }

    /// <inheritdoc />
    public ValueTask<RecordPage> ListAsync(
        CoordinationKey prefix,
        RecordPageRequest page,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(page);
        InProcessGuard.Check(options, cancellationToken);

        var after = ParseContinuationToken(page);

        lock (table.Gate)
        {
            var records = new List<StoredRecord>();
            var more = false;
            long lastSequence = 0;

            foreach (var entry in table.ListLive(prefix))
            {
                if (entry.Sequence <= after)
                {
                    continue;
                }

                if (records.Count == page.Limit)
                {
                    more = true;
                    break;
                }

                records.Add(entry.Record);
                lastSequence = entry.Sequence;
            }

            var result = new RecordPage(table.Current(prefix.Keyspace), records)
            {
                ContinuationToken = more
                    ? lastSequence.ToString(CultureInfo.InvariantCulture)
                    : null,
            };

            return ValueTask.FromResult(result);
        }
    }

    private RecordWriteResult Write(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime
    )
    {
        var revision = table.Advance(key.Keyspace);
        table.Put(
            new StoredRecord(key, value, revision),
            InProcessExpiry.ForLifetime(table.Time, lifetime)
        );
        return RecordWriteResult.Written(revision);
    }

    private static long ParseContinuationToken(RecordPageRequest page)
    {
        if (page.ContinuationToken is null)
        {
            return 0;
        }

        if (
            !long.TryParse(
                page.ContinuationToken,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var after
            )
            || after < 1
        )
        {
            throw new ArgumentException(
                "The continuation token was not issued by the in-process provider.",
                nameof(page)
            );
        }

        return after;
    }
}
