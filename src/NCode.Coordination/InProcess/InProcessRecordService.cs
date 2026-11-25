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

    /// <summary>
    /// Gets the record table, shared with the transaction service.
    /// </summary>
    private InProcessRecordTable Table { get; } = table;

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

        lock (Table.Gate)
        {
            if (lifetime.Session is not null)
            {
                return ValueTask.FromResult(RecordWriteResult.SessionEnded);
            }

            if (Table.Find(key) is not null)
            {
                return ValueTask.FromResult(RecordWriteResult.Conflict);
            }

            var result = Write(key, value, lifetime);
            return ValueTask.FromResult(result);
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

        lock (Table.Gate)
        {
            var current = Table.Find(key);
            return ValueTask.FromResult(current);
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

        lock (Table.Gate)
        {
            if (lifetime.Session is not null)
            {
                return ValueTask.FromResult(RecordWriteResult.SessionEnded);
            }

            var current = Table.Find(key);
            if (current is null)
            {
                return ValueTask.FromResult(RecordWriteResult.NotFound);
            }

            if (current.Revision != expected)
            {
                return ValueTask.FromResult(RecordWriteResult.Conflict);
            }

            var result = Write(key, value, lifetime);
            return ValueTask.FromResult(result);
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

        lock (Table.Gate)
        {
            var current = Table.Find(key);
            if (current is null)
            {
                return ValueTask.FromResult(RecordDeleteStatus.NotFound);
            }

            if (current.Revision != expected)
            {
                return ValueTask.FromResult(RecordDeleteStatus.Conflict);
            }

            Table.Remove(key);
            Table.Advance(key.Keyspace);
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

        lock (Table.Gate)
        {
            var current = Table.Find(key);
            if (current is null)
            {
                return ValueTask.FromResult<StoredRecord?>(null);
            }

            Table.Remove(key);
            Table.Advance(key.Keyspace);
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

        lock (Table.Gate)
        {
            var records = new List<StoredRecord>();
            var more = false;
            long lastSequence = 0;

            foreach (var entry in Table.ListLive(prefix))
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

            var revision = Table.Current(prefix.Keyspace);
            var result = new RecordPage(revision, records)
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
        var revision = Table.Advance(key.Keyspace);
        var record = new StoredRecord(key, value, revision);
        var expiry = InProcessExpiry.ForLifetime(Table.Time, lifetime);
        Table.Put(record, expiry);
        return RecordWriteResult.Written(revision);
    }
}
