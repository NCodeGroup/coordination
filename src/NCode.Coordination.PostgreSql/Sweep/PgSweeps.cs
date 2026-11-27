// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Sweep;

/// <summary>
/// The passes that delete expired rows of the provider's tables, each in bounded batches that skip the rows another
/// transaction holds.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="settings">The settings, whose sweep batch size bounds every pass's statement.</param>
/// <exception cref="ArgumentNullException">The server or the settings are null.</exception>
internal sealed class PgSweeps(PgServer server, PgSettings settings)
{
    /// <summary>The statement that deletes expired occurrences.</summary>
    private const string OccurrencesSql = """
        DELETE FROM {schema}.occurrences
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.occurrences
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes expired counters.</summary>
    private const string CountersSql = """
        DELETE FROM {schema}.counters
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.counters
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes expired set members.</summary>
    private const string SetMembersSql = """
        DELETE FROM {schema}.set_members
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.set_members
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes the rows of sets that hold no member.</summary>
    private const string SetsSql = """
        DELETE FROM {schema}.sets
        WHERE ctid = ANY (ARRAY(
          SELECT h.ctid FROM {schema}.sets h
          WHERE NOT EXISTS (
            SELECT 1 FROM {schema}.set_members m
            WHERE m.keyspace_id = h.keyspace_id AND m.idx = h.idx AND m.depth = h.depth)
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>
    /// Gets the seam to the server.
    /// </summary>
    private PgServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Gets the settings.
    /// </summary>
    private PgSettings Settings { get; } =
        settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>
    /// Gets the passes' statements, in the order they run.
    /// </summary>
    private string[] Passes { get; } =
    [
        settings.Schema.Apply(OccurrencesSql),
        settings.Schema.Apply(CountersSql),
        settings.Schema.Apply(SetMembersSql),
        settings.Schema.Apply(SetsSql),
    ];

    /// <summary>
    /// Runs each pass once, in order. A pass that fails doesn't stop the passes after it.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after every pass ran.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask SweepAsync(CancellationToken cancellationToken)
    {
        foreach (var sql in Passes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await Server
                    .ExecuteAsync(
                        [new PgStatement(sql, PgArgument.Of(Settings.SweepBatchSize))],
                        (_, _) => ValueTask.FromResult(true),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (CoordinationException) { }
            catch (PgKeyspaceMissingException) { }
        }
    }
}
