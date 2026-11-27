// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

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
    /// The operation is not implemented until the batch step.
    /// </remarks>
    public ValueTask<CounterBatchResult> IncrementBatchAsync(
        IReadOnlyList<CounterIncrement> increments,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }
}
