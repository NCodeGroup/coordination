// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Counters;

/// <summary>
/// The counters of the PostgreSQL provider: each is one row of the counters table that expires with the time to live
/// of the increment that created or restarted it, whose deadline the server's clock decides.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="keyspaces">The resolver of keyspace ids.</param>
/// <param name="schema">The schema that holds the counters table.</param>
/// <exception cref="ArgumentNullException">The server or the keyspaces are null.</exception>
internal sealed class PgCounterService(PgServer server, PgKeyspaces keyspaces, PgSchemaName schema)
    : ICounterService
{
    /// <summary>
    /// The statement that adds an amount to a counter, creating it or restarting it when none exists or the one that
    /// exists has expired.
    /// </summary>
    private const string IncrementSql = """
        WITH now AS (SELECT clock_timestamp() AS t)
        INSERT INTO {schema}.counters AS c (keyspace_id, idx, depth, key, count, deadline)
        SELECT $1, $2, $3, $4, $5, now.t + $6 * interval '1 microsecond' FROM now
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET
          count = CASE WHEN c.deadline <= (SELECT t FROM now) THEN EXCLUDED.count ELSE c.count + EXCLUDED.count END,
          deadline = CASE WHEN c.deadline <= (SELECT t FROM now) THEN EXCLUDED.deadline ELSE c.deadline END
        RETURNING count
        """;

    /// <summary>
    /// The statement that reads the count of an unexpired counter.
    /// </summary>
    private const string ReadSql = """
        SELECT count FROM {schema}.counters
        WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND deadline > clock_timestamp()
        """;

    /// <summary>
    /// The statement that deletes a counter.
    /// </summary>
    private const string ResetSql = """
        DELETE FROM {schema}.counters WHERE keyspace_id = $1 AND idx = $2 AND depth = $3
        """;

    /// <summary>
    /// The statement that takes the row lock of every counter of a batch in key order, inserting an expired row for
    /// each one that has none, and returns each row's count and deadline.
    /// </summary>
    private const string BatchUpsertSql = """
        INSERT INTO {schema}.counters AS c (keyspace_id, idx, depth, key, count, deadline)
        SELECT $1, k.idx, k.depth, k.key, 0, timestamptz '1970-01-01 00:00:00+00'
        FROM unnest($2::bytea[], $3::smallint[], $4::text[]) AS k(idx, depth, key)
        ORDER BY k.idx, k.depth
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET key = EXCLUDED.key
        RETURNING idx, depth, count, deadline
        """;

    /// <summary>
    /// The statement that reads the server's clock.
    /// </summary>
    private const string BatchClockSql = "SELECT clock_timestamp()";

    /// <summary>
    /// The statement that writes the counts and deadlines that a batch changed.
    /// </summary>
    private const string BatchCommitSql = """
        UPDATE {schema}.counters AS c SET count = u.count, deadline = u.deadline
        FROM unnest($2::bytea[], $3::smallint[], $4::bigint[], $5::timestamptz[]) AS u(idx, depth, count, deadline)
        WHERE c.keyspace_id = $1 AND c.idx = u.idx AND c.depth = u.depth
        """;

    /// <summary>
    /// Reads the counts that a statement returned.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The counts, in the order the server sent them.</returns>
    private static async ValueTask<ImmutableArray<long>> ReadCountsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results
            .ReadAsync(reader => reader.GetInt64(0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the results of a transaction's first batch: the locked rows, then the server's clock.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The rows of the batch's counters and the instant of the server's clock.</returns>
    private static async ValueTask<(
        ImmutableArray<PgCounterRow> Rows,
        DateTime Now
    )> ReadLockedAsync(PgResults results, CancellationToken cancellationToken)
    {
        var rows = await results.ReadAsync(ReadRow, cancellationToken).ConfigureAwait(false);
        var clock = await results.ReadAsync(ReadClock, cancellationToken).ConfigureAwait(false);

        return (rows, clock[0]);
    }

    /// <summary>
    /// Reads a row that the batch's lock statement returned.
    /// </summary>
    /// <param name="reader">The reader, at a row.</param>
    /// <returns>The row.</returns>
    private static PgCounterRow ReadRow(NpgsqlDataReader reader)
    {
        var index = reader.GetFieldValue<byte[]>(0);
        var depth = reader.GetInt16(1);
        var count = reader.GetInt64(2);
        var deadline = reader.GetDateTime(3);

        return new PgCounterRow(index, depth, count, deadline);
    }

    /// <summary>
    /// Reads the row that the clock statement returned.
    /// </summary>
    /// <param name="reader">The reader, at a row.</param>
    /// <returns>The instant of the server's clock.</returns>
    private static DateTime ReadClock(NpgsqlDataReader reader)
    {
        return reader.GetDateTime(0);
    }

    /// <summary>
    /// Checks the arguments of a batch.
    /// </summary>
    /// <param name="increments">The increments of the batch.</param>
    /// <exception cref="ArgumentNullException">The increments are null.</exception>
    /// <exception cref="ArgumentException">
    /// The batch has too few or too many increments, holds a null one, or spans keyspaces.
    /// </exception>
    private static void ValidateBatch(IReadOnlyList<CounterIncrement> increments)
    {
        ArgumentNullException.ThrowIfNull(increments);

        if (increments.Count < 1 || increments.Count > CoordinationLimits.MaxBatchIncrements)
        {
            throw new ArgumentException(
                $"A batch has 1 to {CoordinationLimits.MaxBatchIncrements} increments.",
                nameof(increments)
            );
        }

        ValidateOneKeyspace(increments);
    }

    /// <summary>
    /// Checks that no increment of a batch is null and that every one is in one keyspace.
    /// </summary>
    /// <param name="increments">The increments of the batch.</param>
    /// <exception cref="ArgumentException">The batch holds a null increment or spans keyspaces.</exception>
    private static void ValidateOneKeyspace(IReadOnlyList<CounterIncrement> increments)
    {
        Keyspace? keyspace = null;
        foreach (var increment in increments)
        {
            if (increment is null)
            {
                throw new ArgumentException("A batch holds no null increment.", nameof(increments));
            }

            keyspace ??= increment.Key.Keyspace;
            if (increment.Key.Keyspace != keyspace)
            {
                throw new ArgumentException(
                    "Every increment of a batch is in one keyspace.",
                    nameof(increments)
                );
            }
        }
    }

    /// <summary>
    /// Builds the entry of each distinct key of a batch, in the order the rows are locked in.
    /// </summary>
    /// <param name="increments">The increments of the batch.</param>
    /// <returns>The batch, whose entries are in key order.</returns>
    private static PgCounterBatch BuildBatch(IReadOnlyList<CounterIncrement> increments)
    {
        var entries = new Dictionary<CoordinationKey, PgCounterEntry>(increments.Count);
        foreach (var increment in increments)
        {
            if (!entries.ContainsKey(increment.Key))
            {
                entries[increment.Key] = CreateEntry(increment.Key);
            }
        }

        var ordered = new List<PgCounterEntry>(entries.Values);
        ordered.Sort(CompareEntries);

        return new PgCounterBatch([.. increments], [.. ordered]);
    }

    /// <summary>
    /// Creates the entry of a key.
    /// </summary>
    /// <param name="key">The key of a counter.</param>
    /// <returns>The entry, which holds the key's index value, depth and text.</returns>
    private static PgCounterEntry CreateEntry(CoordinationKey key)
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var text = PgKeys.KeyText(key);

        return new PgCounterEntry(key, index, depth, text);
    }

    /// <summary>
    /// Compares two entries in the order the server locks rows in: by index value, then by depth.
    /// </summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns>A value that is negative, zero or positive as the first entry is before, equal to or after the second.</returns>
    private static int CompareEntries(PgCounterEntry left, PgCounterEntry right)
    {
        var order = left.Index.AsSpan().SequenceCompareTo(right.Index);
        return order != 0 ? order : left.Depth.CompareTo(right.Depth);
    }

    /// <summary>
    /// Decides a batch exactly as the in-process service does: adds each increment in order to the count of its
    /// counter, which is 0 when the counter has no unexpired row, and stops at the first count past its limit.
    /// </summary>
    /// <param name="batch">The batch.</param>
    /// <param name="rows">The rows of the batch's counters, as the lock statement returned them.</param>
    /// <param name="instant">The instant of the server's clock.</param>
    /// <returns>The result of the batch and the rows to write, or the decision that a sum passes the largest count.</returns>
    private static PgCounterDecision Decide(
        PgCounterBatch batch,
        ImmutableArray<PgCounterRow> rows,
        DateTime instant
    )
    {
        var stored = FindUnexpired(rows, instant);
        var entries = IndexEntries(batch.Entries);
        var pending = new Dictionary<CoordinationKey, PgCounterWrite>(batch.Increments.Length);
        var counts = new List<long>(batch.Increments.Length);
        int? exceeded = null;

        for (var position = 0; position < batch.Increments.Length; position++)
        {
            var increment = batch.Increments[position];
            if (!pending.TryGetValue(increment.Key, out var write))
            {
                var entry = entries[increment.Key];
                write = StartWrite(entry, increment.TimeToLive, stored, instant);
            }

            if (write.Count > long.MaxValue - increment.Amount)
            {
                return PgCounterDecision.Overflow;
            }

            var added = write with { Count = write.Count + increment.Amount };
            pending[increment.Key] = added;
            counts.Add(added.Count);

            if (added.Count > increment.Limit)
            {
                exceeded = position;
                break;
            }
        }

        var result = new CounterBatchResult(counts) { ExceededIndex = exceeded };
        return new PgCounterDecision(result, [.. pending.Values]);
    }

    /// <summary>
    /// Finds the entry of each key of a batch.
    /// </summary>
    /// <param name="entries">The entries of the batch.</param>
    /// <returns>The entries by key.</returns>
    private static Dictionary<CoordinationKey, PgCounterEntry> IndexEntries(
        ImmutableArray<PgCounterEntry> entries
    )
    {
        var byKey = new Dictionary<CoordinationKey, PgCounterEntry>(entries.Length);
        foreach (var entry in entries)
        {
            byKey[entry.Key] = entry;
        }

        return byKey;
    }

    /// <summary>
    /// Finds the rows that have not expired at an instant.
    /// </summary>
    /// <param name="rows">The rows of the batch's counters.</param>
    /// <param name="instant">The instant of the server's clock.</param>
    /// <returns>The unexpired rows by identity, whose deadlines are UTC.</returns>
    private static Dictionary<PgRowIdentity, PgCounterRow> FindUnexpired(
        ImmutableArray<PgCounterRow> rows,
        DateTime instant
    )
    {
        var unexpired = new Dictionary<PgRowIdentity, PgCounterRow>(rows.Length);
        foreach (var row in rows)
        {
            var deadline = DateTime.SpecifyKind(row.Deadline, DateTimeKind.Utc);
            if (deadline > instant)
            {
                unexpired[row.Identity] = row with { Deadline = deadline };
            }
        }

        return unexpired;
    }

    /// <summary>
    /// Starts the write of a counter that a batch first touches: its unexpired row when it has one, otherwise a count
    /// of 0 that expires one time to live after the instant.
    /// </summary>
    /// <param name="entry">The counter's entry.</param>
    /// <param name="timeToLive">The time to live of the increment that first touches the counter.</param>
    /// <param name="stored">The unexpired rows by identity.</param>
    /// <param name="instant">The instant of the server's clock.</param>
    /// <returns>The write, before the increment is added.</returns>
    private static PgCounterWrite StartWrite(
        PgCounterEntry entry,
        TimeToLive timeToLive,
        Dictionary<PgRowIdentity, PgCounterRow> stored,
        DateTime instant
    )
    {
        if (stored.TryGetValue(entry.Identity, out var live))
        {
            return new PgCounterWrite(entry, live.Count, live.Deadline);
        }

        var deadline = PgCall.DeadlineAfter(instant, timeToLive);
        return new PgCounterWrite(entry, 0, deadline);
    }

    /// <summary>
    /// Gets the seam to the server.
    /// </summary>
    private PgServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the resolver of keyspace ids.
    /// </summary>
    private PgKeyspaces Keyspaces { get; } =
        keyspaces ?? throw new ArgumentNullException(nameof(keyspaces));

    /// <summary>
    /// Gets the statement that adds an amount to a counter, creating it or restarting it when none exists or the one
    /// that exists has expired.
    /// </summary>
    private string Increment { get; } = schema.Apply(IncrementSql);

    /// <summary>
    /// Gets the statement that reads the count of an unexpired counter.
    /// </summary>
    private string Read { get; } = schema.Apply(ReadSql);

    /// <summary>
    /// Gets the statement that deletes a counter.
    /// </summary>
    private string Reset { get; } = schema.Apply(ResetSql);

    /// <summary>
    /// Gets the statement that takes the row lock of every counter of a batch and returns each row.
    /// </summary>
    private string BatchUpsert { get; } = schema.Apply(BatchUpsertSql);

    /// <summary>
    /// Gets the statement that reads the server's clock.
    /// </summary>
    private string BatchClock { get; } = BatchClockSql;

    /// <summary>
    /// Gets the statement that writes the counts and deadlines that a batch changed.
    /// </summary>
    private string BatchCommit { get; } = schema.Apply(BatchCommitSql);

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one upsert whose time to live starts only with the increment that creates or restarts the
    /// counter. A sum past <see cref="long.MaxValue"/> fails with <c>22003</c>, which the seam reports as
    /// <c>limit-exceeded</c> and not applied.
    /// </remarks>
    public async ValueTask<long> IncrementAsync(
        CoordinationKey key,
        long amount,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeToLive);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        PgCall.Check(options, cancellationToken);

        var microseconds = PgCall.ToMicroseconds(timeToLive);

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => IncrementInKeyspaceAsync(id, key, amount, microseconds, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one select of the unexpired row, and a counter without one counts 0.
    /// </remarks>
    public async ValueTask<long> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => ReadInKeyspaceAsync(id, key, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one delete of the row, which succeeds when none exists.
    /// </remarks>
    public async ValueTask ResetAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => ResetInKeyspaceAsync(id, key, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one transaction. Its first batch takes the row lock of every counter in key order and reads
    /// the server's clock, the decision is made here exactly as the in-process service makes it, and its second batch
    /// writes the changed counters and commits. A sum past <see cref="long.MaxValue"/> rolls back and fails as
    /// <c>limit-exceeded</c>, not applied.
    /// </remarks>
    public async ValueTask<CounterBatchResult> IncrementBatchAsync(
        IReadOnlyList<CounterIncrement> increments,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ValidateBatch(increments);
        PgCall.Check(options, cancellationToken);

        var keyspace = increments[0].Key.Keyspace;
        var batch = BuildBatch(increments);

        return await Keyspaces
            .RunAsync(
                keyspace,
                (id, token) => IncrementBatchInKeyspaceAsync(id, batch, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adds an amount to a counter in a keyspace in one statement.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the counter.</param>
    /// <param name="amount">The amount to add.</param>
    /// <param name="microseconds">The time to live, in whole microseconds.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The count after the addition.</returns>
    private async ValueTask<long> IncrementInKeyspaceAsync(
        long id,
        CoordinationKey key,
        long amount,
        long microseconds,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var text = PgKeys.KeyText(key);

        var statement = new PgStatement(Increment, id, index, depth, text, amount, microseconds);
        var rows = await Server
            .ExecuteAsync([statement], ReadCountsAsync, cancellationToken)
            .ConfigureAwait(false);

        return rows[0];
    }

    /// <summary>
    /// Reads the count of an unexpired counter in a keyspace.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the counter.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The count, or 0 when the counter has no unexpired row.</returns>
    private async ValueTask<long> ReadInKeyspaceAsync(
        long id,
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        var statement = new PgStatement(Read, id, index, depth);
        var rows = await Server
            .ExecuteAsync([statement], ReadCountsAsync, cancellationToken)
            .ConfigureAwait(false);

        return rows.Length > 0 ? rows[0] : 0L;
    }

    /// <summary>
    /// Deletes a counter in a keyspace in one statement.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the counter.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the statement has run.</returns>
    private async ValueTask ResetInKeyspaceAsync(
        long id,
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        var statement = new PgStatement(Reset, id, index, depth);
        await Server
            .ExecuteAsync([statement], PgResults.IgnoreAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a batch of increments in one transaction on one connection.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="batch">The batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the batch.</returns>
    private async ValueTask<CounterBatchResult> IncrementBatchInKeyspaceAsync(
        long id,
        PgCounterBatch batch,
        CancellationToken cancellationToken
    )
    {
        return await Server
            .TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => ApplyBatchAsync(transaction, id, batch, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Locks the batch's rows, decides the batch, and writes what it decided, or rolls back when a sum would pass
    /// the largest count.
    /// </summary>
    /// <param name="transaction">The transaction that runs the batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="batch">The batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The result of the batch.</returns>
    /// <exception cref="CoordinationException">A sum would pass the largest count.</exception>
    private async ValueTask<CounterBatchResult> ApplyBatchAsync(
        PgTransaction transaction,
        long id,
        PgCounterBatch batch,
        CancellationToken cancellationToken
    )
    {
        var (rows, now) = await LockRowsAsync(transaction, id, batch, cancellationToken)
            .ConfigureAwait(false);

        var instant = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var decision = Decide(batch, rows, instant);
        if (decision.Result is not { } result)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new CoordinationException(
                CoordinationErrorCodes.LimitExceeded,
                outcomeUnknown: false,
                "The counter would pass the largest count."
            );
        }

        var commitStatement = CreateCommitStatement(id, decision.Writes);
        await transaction.CommitAsync([commitStatement], cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Sends the transaction's first batch, which takes the row lock of every counter of a batch in key order and
    /// reads the server's clock.
    /// </summary>
    /// <param name="transaction">The transaction that runs the batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="batch">The batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The rows of the batch's counters and the instant of the server's clock.</returns>
    private async ValueTask<(ImmutableArray<PgCounterRow> Rows, DateTime Now)> LockRowsAsync(
        PgTransaction transaction,
        long id,
        PgCounterBatch batch,
        CancellationToken cancellationToken
    )
    {
        var lockStatement = CreateLockStatement(id, batch);
        var clockStatement = new PgStatement(BatchClock);

        return await transaction
            .FirstAsync([lockStatement, clockStatement], ReadLockedAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the statement that takes the row lock of every counter of a batch, whose array arguments are in key
    /// order.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="batch">The batch.</param>
    /// <returns>The statement.</returns>
    private PgStatement CreateLockStatement(long id, PgCounterBatch batch)
    {
        var count = batch.Entries.Length;
        var indexes = new byte[count][];
        var depths = new short[count];
        var texts = new string[count];
        for (var position = 0; position < count; position++)
        {
            var entry = batch.Entries[position];
            indexes[position] = entry.Index;
            depths[position] = entry.Depth;
            texts[position] = entry.Text;
        }

        var indexesArgument = PgArgument.Typed(indexes, NpgsqlDbType.Array | NpgsqlDbType.Bytea);
        var depthsArgument = PgArgument.Typed(depths, NpgsqlDbType.Array | NpgsqlDbType.Smallint);
        var textsArgument = PgArgument.Typed(texts, NpgsqlDbType.Array | NpgsqlDbType.Text);

        return new PgStatement(BatchUpsert, id, indexesArgument, depthsArgument, textsArgument);
    }

    /// <summary>
    /// Creates the statement that writes the counts and deadlines that a batch changed, whose array arguments hold
    /// one element per counter.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="writes">The counters to write.</param>
    /// <returns>The statement.</returns>
    private PgStatement CreateCommitStatement(long id, ImmutableArray<PgCounterWrite> writes)
    {
        var count = writes.Length;
        var indexes = new byte[count][];
        var depths = new short[count];
        var counts = new long[count];
        var deadlines = new DateTime[count];
        for (var position = 0; position < count; position++)
        {
            var write = writes[position];
            indexes[position] = write.Entry.Index;
            depths[position] = write.Entry.Depth;
            counts[position] = write.Count;
            deadlines[position] = write.Deadline;
        }

        var indexesArgument = PgArgument.Typed(indexes, NpgsqlDbType.Array | NpgsqlDbType.Bytea);
        var depthsArgument = PgArgument.Typed(depths, NpgsqlDbType.Array | NpgsqlDbType.Smallint);
        var countsArgument = PgArgument.Typed(counts, NpgsqlDbType.Array | NpgsqlDbType.Bigint);
        var deadlinesArgument = PgArgument.Typed(
            deadlines,
            NpgsqlDbType.Array | NpgsqlDbType.TimestampTz
        );

        return new PgStatement(
            BatchCommit,
            id,
            indexesArgument,
            depthsArgument,
            countsArgument,
            deadlinesArgument
        );
    }
}
