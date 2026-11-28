// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Claims;

/// <summary>
/// The claims of the PostgreSQL provider: each is one row of the claims table, named by the claim's name and window,
/// whose deadline the server's clock decides.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="keyspaces">The resolver of keyspace ids.</param>
/// <param name="schema">The schema that holds the claims table.</param>
/// <exception cref="ArgumentNullException">The server or the keyspaces are null.</exception>
internal sealed class PgClaimService(PgServer server, PgKeyspaces keyspaces, PgSchemaName schema)
    : IClaimService
{
    /// <summary>
    /// The statement that records a claim unless an unexpired one exists.
    /// </summary>
    private const string ClaimSql = """
        WITH now AS (SELECT clock_timestamp() AS t)
        INSERT INTO {schema}.claims AS c (keyspace_id, idx, depth, key, deadline)
        SELECT $1, $2, $3, $4, now.t + $5 * interval '1 microsecond' FROM now
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET key = EXCLUDED.key, deadline = EXCLUDED.deadline
        WHERE c.deadline <= (SELECT t FROM now)
        RETURNING 1
        """;

    /// <summary>
    /// Reads the rows that the claim statement returned.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>One value for the row that was written, or none when an unexpired claim exists.</returns>
    private static async ValueTask<ImmutableArray<int>> ReadClaimedAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results
            .ReadAsync(reader => reader.GetInt32(0), cancellationToken)
            .ConfigureAwait(false);
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
    /// Gets the statement that records a claim unless an unexpired one exists.
    /// </summary>
    private string Claim { get; } = schema.Apply(ClaimSql);

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one upsert on the row of the name followed by the window's segment, which writes the row when
    /// none exists or the one that exists has expired. It fails closed: a returned row is claimed, none is taken, and
    /// every failure throws.
    /// </remarks>
    /// <exception cref="ArgumentException">The name has no room for the window's segment.</exception>
    public async ValueTask<ClaimStatus> TryClaimAsync(
        CoordinationKey name,
        TimeWindow window,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(timeToLive);

        if (name.Segments.Length >= CoordinationKey.MaxSegments)
        {
            throw new ArgumentException(
                $"A claim's name has at most {CoordinationKey.MaxSegments - 1} segments, so the window's segment fits after them.",
                nameof(name)
            );
        }

        PgCall.Check(options, cancellationToken);

        var key = new CoordinationKey(name.Keyspace, [.. name.Segments, window.Segment])
        {
            PartitionDepth = name.PartitionDepth,
        };

        var microseconds = PgCall.ToMicroseconds(timeToLive);

        var claimed = await Keyspaces
            .RunAsync(
                key.Keyspace,
                (id, token) => ClaimInKeyspaceAsync(id, key, microseconds, token),
                cancellationToken
            )
            .ConfigureAwait(false);

        return claimed ? ClaimStatus.Claimed : ClaimStatus.Taken;
    }

    /// <summary>
    /// Records a claim in a keyspace unless an unexpired one exists.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="key">The key of the claim: its name followed by the window's segment.</param>
    /// <param name="microseconds">The time to live, in whole microseconds.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>True when the claim was recorded; false when an unexpired one exists.</returns>
    private async ValueTask<bool> ClaimInKeyspaceAsync(
        long id,
        CoordinationKey key,
        long microseconds,
        CancellationToken cancellationToken
    )
    {
        var (index, text) = PgKeys.IndexAndText(key);
        var depth = (short)key.PartitionDepth;

        var statement = new PgStatement(Claim, id, index, depth, text, microseconds);
        var rows = await Server
            .ExecuteAsync([statement], ReadClaimedAsync, cancellationToken)
            .ConfigureAwait(false);

        return rows.Length > 0;
    }
}
