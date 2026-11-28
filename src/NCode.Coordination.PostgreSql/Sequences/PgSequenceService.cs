// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Sequences;

/// <summary>
/// The sequences of the PostgreSQL provider: each is one row of the sequences table that is never reset or expired.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="keyspaces">The resolver of keyspace ids.</param>
/// <param name="schema">The schema that holds the sequences table.</param>
/// <exception cref="ArgumentNullException">The server or the keyspaces are null.</exception>
internal sealed class PgSequenceService(PgServer server, PgKeyspaces keyspaces, PgSchemaName schema)
    : ISequenceService
{
    /// <summary>The statement that adds an amount to a sequence, creating it when none exists.</summary>
    private const string AddSql = """
        INSERT INTO {schema}.sequences AS s (keyspace_id, idx, depth, key, value)
        VALUES ($1, $2, $3, $4, $5)
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET value = s.value + EXCLUDED.value
        RETURNING value
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
    /// Gets the statement that adds an amount to a sequence, creating it when none exists.
    /// </summary>
    private string Add { get; } = schema.Apply(AddSql);

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one upsert that adds one and returns the new value.
    /// </remarks>
    public async ValueTask<long> NextAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        PgCall.Check(options, cancellationToken);

        return await AddAsync(key, 1, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one upsert that adds the count, whose result is the last number of the block.
    /// </remarks>
    public async ValueTask<SequenceBlock> ReserveAsync(
        CoordinationKey key,
        int count,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, CoordinationLimits.MaxSequenceBlock);
        PgCall.Check(options, cancellationToken);

        var last = await AddAsync(key, count, cancellationToken).ConfigureAwait(false);

        return new SequenceBlock(last - count + 1, count);
    }

    /// <summary>
    /// Adds an amount to a sequence in one statement.
    /// </summary>
    /// <param name="key">The key of the sequence.</param>
    /// <param name="amount">The amount to add.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The value of the sequence after the addition.</returns>
    /// <exception cref="CoordinationException">The server failed, or the value would pass <see cref="long.MaxValue"/>.</exception>
    private async ValueTask<long> AddAsync(
        CoordinationKey key,
        long amount,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var text = PgKeys.KeyText(key);

        return await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                {
                    var rows = await Server
                        .ExecuteAsync(
                            [new PgStatement(Add, id, index, depth, text, amount)],
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
}
