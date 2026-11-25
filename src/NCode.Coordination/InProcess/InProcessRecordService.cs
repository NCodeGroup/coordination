// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace NCode.Coordination.InProcess;

/// <summary>
/// The records of the in-process provider.
/// </summary>
/// <param name="sessions">The session table, which holds the record table that the transaction service shares.</param>
/// <remarks>
/// Every operation runs entirely while holding the table's lock, so it is linearizable in the process.
/// </remarks>
internal class InProcessRecordService(InProcessSessionTable sessions) : IRecordService
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
    private InProcessRecordTable Table => Sessions.Records;

    /// <summary>
    /// Gets the session table, which says whether a session is live.
    /// </summary>
    private InProcessSessionTable Sessions { get; } = sessions;

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

        using var scope = Table.Enter();

        if (lifetime.Session is { } session && !Sessions.IsLive(session))
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

    /// <inheritdoc />
    public ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        InProcessGuard.Check(options, cancellationToken);

        using var scope = Table.Enter();

        var current = Table.Find(key);
        return ValueTask.FromResult(current);
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

        using var scope = Table.Enter();

        if (lifetime.Session is { } session && !Sessions.IsLive(session))
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

        using var scope = Table.Enter();

        var current = Table.Find(key);
        if (current is null)
        {
            return ValueTask.FromResult(RecordDeleteStatus.NotFound);
        }

        if (current.Revision != expected)
        {
            return ValueTask.FromResult(RecordDeleteStatus.Conflict);
        }

        var revision = Table.Advance(key.Keyspace);

        Table.Remove(key, revision);
        return ValueTask.FromResult(RecordDeleteStatus.Deleted);
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

        using var scope = Table.Enter();

        var current = Table.Find(key);
        if (current is null)
        {
            return ValueTask.FromResult<StoredRecord?>(null);
        }

        var revision = Table.Advance(key.Keyspace);

        Table.Remove(key, revision);
        return ValueTask.FromResult<StoredRecord?>(current);
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

        using var scope = Table.Enter();

        var live = Table.ListLive(prefix, after, page.Limit + 1);
        var more = live.Count > page.Limit;
        var returned = more ? page.Limit : live.Count;
        var records = new List<StoredRecord>(returned);
        long lastSequence = 0;

        for (var index = 0; index < returned; index++)
        {
            var entry = live[index];

            records.Add(entry.Record);
            lastSequence = entry.Sequence;
        }

        var revision = Table.Current(prefix.Keyspace);
        var token = more ? lastSequence.ToString(CultureInfo.InvariantCulture) : null;
        var result = new RecordPage(revision, records) { ContinuationToken = token };

        return ValueTask.FromResult(result);
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
