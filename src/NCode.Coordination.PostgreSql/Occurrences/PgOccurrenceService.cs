// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Occurrences;

/// <summary>
/// The occurrences of the PostgreSQL provider: each is one row of the occurrences table, whose deadline the server's
/// clock decides.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="keyspaces">The resolver of keyspace ids.</param>
/// <param name="schema">The schema that holds the occurrences table.</param>
/// <exception cref="ArgumentNullException">The server or the keyspaces are null.</exception>
internal sealed class PgOccurrenceService(
    PgServer server,
    PgKeyspaces keyspaces,
    PgSchemaName schema
) : IOccurrenceService
{
    /// <summary>The statement that records an occurrence unless an unexpired one exists.</summary>
    private const string RecordSql = """
        WITH now AS (SELECT clock_timestamp() AS t)
        INSERT INTO {schema}.occurrences AS o (keyspace_id, idx, depth, key, deadline)
        SELECT $1, $2, $3, $4, now.t + $5 * interval '1 microsecond' FROM now
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET key = EXCLUDED.key, deadline = EXCLUDED.deadline
        WHERE o.deadline <= (SELECT t FROM now)
        RETURNING 1
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
    /// Gets the statement that records an occurrence unless an unexpired one exists.
    /// </summary>
    private string Record { get; } = schema.Apply(RecordSql);

    /// <inheritdoc />
    /// <remarks>
    /// The operation is one upsert that writes the row when none exists or the one that exists has expired. It fails
    /// closed: a returned row is recorded, none is a duplicate, and every failure throws.
    /// </remarks>
    public async ValueTask<OccurrenceStatus> TryRecordAsync(
        CoordinationKey key,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeToLive);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var text = PgKeys.KeyText(key);
        var microseconds = PgCall.ToMicroseconds(timeToLive);

        var recorded = await Keyspaces
            .RunAsync(
                key.Keyspace,
                async (id, token) =>
                {
                    var rows = await Server
                        .ExecuteAsync(
                            [new PgStatement(Record, id, index, depth, text, microseconds)],
                            async (results, readToken) =>
                                await results
                                    .ReadAsync(reader => reader.GetInt32(0), readToken)
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    return rows.Length > 0;
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return recorded ? OccurrenceStatus.Recorded : OccurrenceStatus.Duplicate;
    }
}
