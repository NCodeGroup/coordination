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
    /// <summary>
    /// Reads the position that the continuation token of a page request names.
    /// </summary>
    /// <param name="page">The page request.</param>
    /// <returns>The write sequence after which the page starts, or 0 when the request has no token.</returns>
    /// <exception cref="ArgumentException">The provider did not issue the continuation token.</exception>
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

        using (Table.Enter())
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

        using (Table.Enter())
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

        using (Table.Enter())
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

        using (Table.Enter())
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

            Table.Remove(key, Table.Advance(key.Keyspace));
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

        using (Table.Enter())
        {
            var current = Table.Find(key);
            if (current is null)
            {
                return ValueTask.FromResult<StoredRecord?>(null);
            }

            Table.Remove(key, Table.Advance(key.Keyspace));
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

        using (Table.Enter())
        {
            var live = Table.ListLive(prefix);
            var capacity = Math.Min(page.Limit, live.Count);
            var records = new List<StoredRecord>(capacity);
            var more = false;
            long lastSequence = 0;

            foreach (var entry in live)
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
            var token = more ? lastSequence.ToString(CultureInfo.InvariantCulture) : null;
            var result = new RecordPage(revision, records) { ContinuationToken = token };

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// Stores a record at the next revision of its keyspace, replacing any record of the key.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="value">The value of the record.</param>
    /// <param name="lifetime">How long the record lives.</param>
    /// <returns>The result that reports the revision the record was written at.</returns>
    private RecordWriteResult Write(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime
    )
    {
        var revision = Table.Advance(key.Keyspace);
        var record = new StoredRecord(key, value, revision);
        Table.Put(record, lifetime);
        return RecordWriteResult.Written(revision);
    }
}
