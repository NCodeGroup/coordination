// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
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

    /// <summary>The statement that reads the count of an unexpired counter.</summary>
    private const string ReadSql = """
        SELECT count FROM {schema}.counters
        WHERE keyspace_id = $1 AND idx = $2 AND depth = $3 AND deadline > clock_timestamp()
        """;

    /// <summary>The statement that deletes a counter.</summary>
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

    /// <summary>The statement that reads the server's clock.</summary>
    private const string BatchClockSql = "SELECT clock_timestamp()";

    /// <summary>The statement that writes the counts and deadlines that a batch changed.</summary>
    private const string BatchCommitSql = """
        UPDATE {schema}.counters AS c SET count = u.count, deadline = u.deadline
        FROM unnest($2::bytea[], $3::smallint[], $4::bigint[], $5::timestamptz[]) AS u(idx, depth, count, deadline)
        WHERE c.keyspace_id = $1 AND c.idx = u.idx AND c.depth = u.depth
        """;

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

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var text = PgKeys.KeyText(key);
        var microseconds = PgCall.ToMicroseconds(timeToLive);

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                {
                    var rows = await Server
                        .ExecuteAsync(
                            [
                                new PgStatement(
                                    Increment,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth),
                                    PgArgument.Of(text),
                                    PgArgument.Of(amount),
                                    PgArgument.Of(microseconds)
                                ),
                            ],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetInt64(0), readToken)
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return rows[0];
                },
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

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                {
                    var rows = await Server
                        .ExecuteAsync(
                            [
                                new PgStatement(
                                    Read,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth)
                                ),
                            ],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetInt64(0), readToken)
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return rows.Length > 0 ? rows[0] : 0L;
                },
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

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;

        await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                    await Server
                        .ExecuteAsync(
                            [
                                new PgStatement(
                                    Reset,
                                    PgArgument.Of(id),
                                    PgArgument.Of(index),
                                    PgArgument.Of(depth)
                                ),
                            ],
                            (_, _) => ValueTask.FromResult(true),
                            token
                        )
                        .ConfigureAwait(false),
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
        ArgumentNullException.ThrowIfNull(increments);

        if (increments.Count < 1 || increments.Count > CoordinationLimits.MaxBatchIncrements)
        {
            throw new ArgumentException(
                $"A batch has 1 to {CoordinationLimits.MaxBatchIncrements} increments.",
                nameof(increments)
            );
        }

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

        PgCall.Check(options, cancellationToken);

        var batchKeyspace = increments[0].Key.Keyspace;
        var entries = new Dictionary<CoordinationKey, (byte[] Index, short Depth, string Text)>();
        foreach (var increment in increments)
        {
            if (!entries.ContainsKey(increment.Key))
            {
                entries[increment.Key] = (
                    PgKeys.Index(increment.Key),
                    (short)increment.Key.PartitionDepth,
                    PgKeys.KeyText(increment.Key)
                );
            }
        }

        var sorted = entries.ToList();
        sorted.Sort(
            (left, right) =>
            {
                var order = left.Value.Index.AsSpan().SequenceCompareTo(right.Value.Index);
                return order != 0 ? order : left.Value.Depth.CompareTo(right.Value.Depth);
            }
        );

        var indexes = sorted.Select(pair => pair.Value.Index).ToArray();
        var depths = sorted.Select(pair => pair.Value.Depth).ToArray();
        var texts = sorted.Select(pair => pair.Value.Text).ToArray();

        return await Keyspaces
            .RunAsync(
                batchKeyspace,
                async (id, runToken) =>
                    await Server
                        .TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, token) =>
                            {
                                var (rows, now) = await transaction
                                    .FirstAsync(
                                        [
                                            new PgStatement(
                                                BatchUpsert,
                                                PgArgument.Of(id),
                                                PgArgument.Typed(
                                                    indexes,
                                                    NpgsqlDbType.Array | NpgsqlDbType.Bytea
                                                ),
                                                PgArgument.Typed(
                                                    depths,
                                                    NpgsqlDbType.Array | NpgsqlDbType.Smallint
                                                ),
                                                PgArgument.Typed(
                                                    texts,
                                                    NpgsqlDbType.Array | NpgsqlDbType.Text
                                                )
                                            ),
                                            new PgStatement(BatchClock),
                                        ],
                                        async (results, t) =>
                                        {
                                            var existing = await results
                                                .ReadAsync(
                                                    reader =>
                                                        (
                                                            Slot: (
                                                                Convert.ToHexString(
                                                                    reader.GetFieldValue<byte[]>(0)
                                                                ),
                                                                reader.GetInt16(1)
                                                            ),
                                                            Count: reader.GetInt64(2),
                                                            Deadline: reader.GetDateTime(3)
                                                        ),
                                                    t
                                                )
                                                .ConfigureAwait(false);
                                            var clock = await results
                                                .ReadAsync(reader => reader.GetDateTime(0), t)
                                                .ConfigureAwait(false);

                                            return (existing, clock[0]);
                                        },
                                        token
                                    )
                                    .ConfigureAwait(false);

                                var instant = DateTime.SpecifyKind(now, DateTimeKind.Utc);
                                var stored =
                                    new Dictionary<
                                        (string Index, short Depth),
                                        (long Count, DateTime Deadline)
                                    >();
                                foreach (var row in rows)
                                {
                                    var deadline = DateTime.SpecifyKind(
                                        row.Deadline,
                                        DateTimeKind.Utc
                                    );
                                    if (deadline > instant)
                                    {
                                        stored[(row.Slot.Item1, row.Slot.Item2)] = (
                                            row.Count,
                                            deadline
                                        );
                                    }
                                }

                                var pending = new Dictionary<CoordinationKey, long>(
                                    increments.Count
                                );
                                var timeToLives = new Dictionary<CoordinationKey, TimeToLive>(
                                    increments.Count
                                );
                                var counts = new List<long>(increments.Count);
                                int? exceeded = null;

                                for (var position = 0; position < increments.Count; position++)
                                {
                                    var increment = increments[position];
                                    timeToLives.TryAdd(increment.Key, increment.TimeToLive);

                                    if (!pending.TryGetValue(increment.Key, out var current))
                                    {
                                        var entry = entries[increment.Key];
                                        current = stored.TryGetValue(
                                            (Convert.ToHexString(entry.Index), entry.Depth),
                                            out var live
                                        )
                                            ? live.Count
                                            : 0;
                                    }

                                    if (current > long.MaxValue - increment.Amount)
                                    {
                                        await transaction
                                            .RollbackAsync(token)
                                            .ConfigureAwait(false);
                                        throw new CoordinationException(
                                            CoordinationErrorCodes.LimitExceeded,
                                            outcomeUnknown: false,
                                            "The counter would pass the largest count."
                                        );
                                    }

                                    var newCount = current + increment.Amount;
                                    pending[increment.Key] = newCount;
                                    counts.Add(newCount);

                                    if (newCount > increment.Limit)
                                    {
                                        exceeded = position;
                                        break;
                                    }
                                }

                                var changedIndexes = new List<byte[]>(pending.Count);
                                var changedDepths = new List<short>(pending.Count);
                                var changedCounts = new List<long>(pending.Count);
                                var changedDeadlines = new List<DateTime>(pending.Count);
                                foreach (var (key, count) in pending)
                                {
                                    var entry = entries[key];
                                    var deadline = stored.TryGetValue(
                                        (Convert.ToHexString(entry.Index), entry.Depth),
                                        out var live
                                    )
                                        ? live.Deadline
                                        : instant.AddTicks(
                                            PgCall.ToMicroseconds(timeToLives[key]) * 10
                                        );

                                    changedIndexes.Add(entry.Index);
                                    changedDepths.Add(entry.Depth);
                                    changedCounts.Add(count);
                                    changedDeadlines.Add(deadline);
                                }

                                await transaction
                                    .CommitAsync(
                                        [
                                            new PgStatement(
                                                BatchCommit,
                                                PgArgument.Of(id),
                                                PgArgument.Typed(
                                                    changedIndexes.ToArray(),
                                                    NpgsqlDbType.Array | NpgsqlDbType.Bytea
                                                ),
                                                PgArgument.Typed(
                                                    changedDepths.ToArray(),
                                                    NpgsqlDbType.Array | NpgsqlDbType.Smallint
                                                ),
                                                PgArgument.Typed(
                                                    changedCounts.ToArray(),
                                                    NpgsqlDbType.Array | NpgsqlDbType.Bigint
                                                ),
                                                PgArgument.Typed(
                                                    changedDeadlines.ToArray(),
                                                    NpgsqlDbType.Array | NpgsqlDbType.TimestampTz
                                                )
                                            ),
                                        ],
                                        token
                                    )
                                    .ConfigureAwait(false);

                                return new CounterBatchResult(counts) { ExceededIndex = exceeded };
                            },
                            runToken
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
