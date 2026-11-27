// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Sweep;

/// <summary>
/// The passes that delete expired rows of the provider's tables, each in bounded batches that skip the rows another
/// transaction holds.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="settings">The settings, whose sweep batch size bounds every pass's statement.</param>
/// <param name="table">The record table, which commits the deletion of expired records as a change.</param>
/// <exception cref="ArgumentNullException">The server, the settings or the table are null.</exception>
internal sealed class PgSweeps(PgServer server, PgSettings settings, PgRecordTable table)
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

    /// <summary>The statement that lists the keyspaces that hold an expired record.</summary>
    private const string ExpiredKeyspacesSql = """
        SELECT DISTINCT keyspace_id FROM {schema}.records
        WHERE deadline <= clock_timestamp()
        LIMIT 100
        """;

    /// <summary>The statement that locks a keyspace's row, unless another transaction holds it, and reads its revision.</summary>
    private const string LockKeyspaceSql = """
        SELECT revision FROM {schema}.keyspaces WHERE id = $1 FOR NO KEY UPDATE SKIP LOCKED
        """;

    /// <summary>The statement that reads the server's instant.</summary>
    private const string ClockSql = "SELECT clock_timestamp()";

    /// <summary>The statement that reads a keyspace's expired records.</summary>
    private const string ExpiredRecordsSql = """
        SELECT idx, depth, key, value, revision, deadline, session_id
        FROM {schema}.records
        WHERE keyspace_id = $1 AND deadline <= clock_timestamp()
        ORDER BY idx, depth
        LIMIT $2
        """;

    /// <summary>The statement that lists the keyspaces that hold an expired session.</summary>
    private const string ExpiredSessionKeyspacesSql = """
        SELECT DISTINCT keyspace_id FROM {schema}.sessions
        WHERE deadline <= clock_timestamp()
        LIMIT 100
        """;

    /// <summary>The statement that reads a keyspace's expired sessions.</summary>
    private const string ExpiredSessionsSql = """
        SELECT id FROM {schema}.sessions
        WHERE keyspace_id = $1 AND deadline <= clock_timestamp()
        ORDER BY id
        LIMIT $2
        """;

    /// <summary>The statement that deletes the records bound to a session that ended and left the log.</summary>
    private const string BoundRecordsSql = """
        DELETE FROM {schema}.records
        WHERE ctid = ANY (ARRAY(
          SELECT b.ctid FROM {schema}.records b
          WHERE b.session_id IS NOT NULL
            AND NOT EXISTS (
              SELECT 1 FROM {schema}.sessions s
              WHERE s.keyspace_id = b.keyspace_id AND s.id = b.session_id)
            AND NOT EXISTS (
              SELECT 1 FROM {schema}.changes c
              WHERE c.keyspace_id = b.keyspace_id AND c.ended_session = b.session_id)
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes the members bound to a session that ended and left the log.</summary>
    private const string BoundMembersSql = """
        DELETE FROM {schema}.members
        WHERE ctid = ANY (ARRAY(
          SELECT b.ctid FROM {schema}.members b
          WHERE b.session_id IS NOT NULL
            AND NOT EXISTS (
              SELECT 1 FROM {schema}.sessions s
              WHERE s.keyspace_id = b.keyspace_id AND s.id = b.session_id)
            AND NOT EXISTS (
              SELECT 1 FROM {schema}.changes c
              WHERE c.keyspace_id = b.keyspace_id AND c.ended_session = b.session_id)
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes the grants bound to a session that ended and left the log.</summary>
    private const string BoundGrantsSql = """
        DELETE FROM {schema}.grants
        WHERE ctid = ANY (ARRAY(
          SELECT b.ctid FROM {schema}.grants b
          WHERE b.session_id IS NOT NULL
            AND NOT EXISTS (
              SELECT 1 FROM {schema}.sessions s
              WHERE s.keyspace_id = b.keyspace_id AND s.id = b.session_id)
            AND NOT EXISTS (
              SELECT 1 FROM {schema}.changes c
              WHERE c.keyspace_id = b.keyspace_id AND c.ended_session = b.session_id)
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes grants past their deadline.</summary>
    private const string GrantsSql = """
        DELETE FROM {schema}.grants
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.grants
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes claims past their deadline.</summary>
    private const string ClaimsSql = """
        DELETE FROM {schema}.claims
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.claims
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>The statement that deletes the entries of the change logs that passed their retention.</summary>
    private const string LogRetentionSql = """
        WITH removed AS (
          DELETE FROM {schema}.changes
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.changes
            WHERE deadline <= clock_timestamp()
            ORDER BY keyspace_id, revision
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING keyspace_id, revision)
        UPDATE {schema}.keyspaces k
        SET log_removed = greatest(k.log_removed, m.revision)
        FROM (SELECT keyspace_id, max(revision) AS revision FROM removed GROUP BY keyspace_id) m
        WHERE k.id = m.keyspace_id
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
    /// Gets the record table, which commits the deletion of expired records as a change.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the statements of the passes that delete rows without a change, in the order they run.
    /// </summary>
    private string[] Passes { get; } =
    [
        settings.Schema.Apply(OccurrencesSql),
        settings.Schema.Apply(CountersSql),
        settings.Schema.Apply(SetMembersSql),
        settings.Schema.Apply(SetsSql),
    ];

    /// <summary>
    /// Gets the statements of the passes that run after the sessions ended, in the order they run.
    /// </summary>
    private string[] PassesAfterSessions { get; } =
    [
        settings.Schema.Apply(BoundRecordsSql),
        settings.Schema.Apply(BoundMembersSql),
        settings.Schema.Apply(BoundGrantsSql),
        settings.Schema.Apply(GrantsSql),
        settings.Schema.Apply(ClaimsSql),
    ];

    /// <summary>Gets the statement that lists the keyspaces that hold an expired session.</summary>
    private string ExpiredSessionKeyspaces { get; } =
        settings.Schema.Apply(ExpiredSessionKeyspacesSql);

    /// <summary>Gets the statement that reads a keyspace's expired sessions.</summary>
    private string ExpiredSessions { get; } = settings.Schema.Apply(ExpiredSessionsSql);

    /// <summary>Gets the statement that lists the keyspaces that hold an expired record.</summary>
    private string ExpiredKeyspaces { get; } = settings.Schema.Apply(ExpiredKeyspacesSql);

    /// <summary>Gets the statement that locks a keyspace's row, unless another transaction holds it.</summary>
    private string LockKeyspace { get; } = settings.Schema.Apply(LockKeyspaceSql);

    /// <summary>Gets the statement that reads a keyspace's expired records.</summary>
    private string ExpiredRecords { get; } = settings.Schema.Apply(ExpiredRecordsSql);

    /// <summary>Gets the statement that deletes the change log entries that passed their retention.</summary>
    private string LogRetention { get; } = settings.Schema.Apply(LogRetentionSql);

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
            await RunPassAsync(sql, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await SweepRecordsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationException) { }
        catch (PgKeyspaceMissingException) { }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await SweepSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationException) { }
        catch (PgKeyspaceMissingException) { }

        foreach (var sql in PassesAfterSessions)
        {
            await RunPassAsync(sql, cancellationToken).ConfigureAwait(false);
        }

        await RunPassAsync(LogRetention, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one pass that is a single statement, and ignores its failure.
    /// </summary>
    /// <param name="sql">The statement, whose only argument is the batch size.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the pass ran.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    private async ValueTask RunPassAsync(string sql, CancellationToken cancellationToken)
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

    /// <summary>
    /// Deletes expired records, a batch for each keyspace that holds some, and logs each batch as one entry. A keyspace
    /// whose row another transaction holds is skipped, and so is one whose records no longer are expired.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after every keyspace was visited.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask SweepRecordsAsync(CancellationToken cancellationToken)
    {
        var keyspaceIds = await Server
            .ExecuteAsync(
                [new PgStatement(ExpiredKeyspaces)],
                async (results, token) =>
                    await results
                        .ReadAsync(reader => reader.GetInt64(0), token)
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var keyspaceId in keyspaceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SweepKeyspaceAsync(keyspaceId, cancellationToken).ConfigureAwait(false);
            }
            catch (CoordinationException) { }
            catch (PgKeyspaceMissingException) { }
        }
    }

    /// <summary>
    /// Deletes a batch of one keyspace's expired records in a transaction that holds the keyspace's row.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the batch was committed, or skipped.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask SweepKeyspaceAsync(long keyspaceId, CancellationToken cancellationToken)
    {
        await Server
            .TransactAsync(
                PgIsolation.ReadCommitted,
                async (transaction, token) =>
                {
                    var (revisions, clocks, rows) = await transaction
                        .FirstAsync(
                            [
                                new PgStatement(LockKeyspace, PgArgument.Of(keyspaceId)),
                                new PgStatement(ClockSql),
                                new PgStatement(
                                    ExpiredRecords,
                                    PgArgument.Of(keyspaceId),
                                    PgArgument.Of(Settings.SweepBatchSize)
                                ),
                            ],
                            async (results, readToken) =>
                            {
                                var revisionRows = await results
                                    .ReadAsync(reader => reader.GetInt64(0), readToken)
                                    .ConfigureAwait(false);
                                var clockRows = await results
                                    .ReadAsync(reader => reader.GetDateTime(0), readToken)
                                    .ConfigureAwait(false);
                                var recordRows = await results
                                    .ReadAsync(
                                        reader => new PgRecordRow(
                                            reader.GetFieldValue<byte[]>(0),
                                            reader.GetInt16(1),
                                            reader.GetString(2),
                                            reader.GetFieldValue<byte[]>(3),
                                            reader.GetInt64(4),
                                            reader.IsDBNull(5)
                                                ? null
                                                : DateTime.SpecifyKind(
                                                    reader.GetDateTime(5),
                                                    DateTimeKind.Utc
                                                ),
                                            reader.IsDBNull(6) ? null : reader.GetString(6)
                                        ),
                                        readToken
                                    )
                                    .ConfigureAwait(false);

                                return (revisionRows, clockRows, recordRows);
                            },
                            token
                        )
                        .ConfigureAwait(false);

                    if (revisions.Length == 0 || rows.Length == 0)
                    {
                        await transaction.RollbackAsync(token).ConfigureAwait(false);
                        return false;
                    }

                    var change = new PgKeyspaceChange(
                        Settings.Schema,
                        keyspaceId,
                        revisions[0],
                        DateTime.SpecifyKind(clocks[0], DateTimeKind.Utc),
                        Settings.LogRetention
                    );
                    change.Expire(rows);

                    await Table
                        .CommitAsync(transaction, change, keyspaceId, token)
                        .ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ends expired sessions, a batch for each keyspace that holds some, as one change for each session. A keyspace
    /// whose row another transaction holds is skipped, and so is one whose sessions no longer are expired.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after every keyspace was visited.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask SweepSessionsAsync(CancellationToken cancellationToken)
    {
        var keyspaceIds = await Server
            .ExecuteAsync(
                [new PgStatement(ExpiredSessionKeyspaces)],
                async (results, token) =>
                    await results
                        .ReadAsync(reader => reader.GetInt64(0), token)
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var keyspaceId in keyspaceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SweepSessionKeyspaceAsync(keyspaceId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CoordinationException) { }
            catch (PgKeyspaceMissingException) { }
        }
    }

    /// <summary>
    /// Ends a batch of one keyspace's expired sessions in a transaction that holds the keyspace's row.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the batch was committed, or skipped.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask SweepSessionKeyspaceAsync(
        long keyspaceId,
        CancellationToken cancellationToken
    )
    {
        await Server
            .TransactAsync(
                PgIsolation.ReadCommitted,
                async (transaction, token) =>
                {
                    var (revisions, clocks, sessions) = await transaction
                        .FirstAsync(
                            [
                                new PgStatement(LockKeyspace, PgArgument.Of(keyspaceId)),
                                new PgStatement(ClockSql),
                                new PgStatement(
                                    ExpiredSessions,
                                    PgArgument.Of(keyspaceId),
                                    PgArgument.Of(Settings.SweepBatchSize)
                                ),
                            ],
                            async (results, readToken) =>
                            {
                                var revisionRows = await results
                                    .ReadAsync(reader => reader.GetInt64(0), readToken)
                                    .ConfigureAwait(false);
                                var clockRows = await results
                                    .ReadAsync(reader => reader.GetDateTime(0), readToken)
                                    .ConfigureAwait(false);
                                var sessionRows = await results
                                    .ReadAsync(reader => reader.GetString(0), readToken)
                                    .ConfigureAwait(false);

                                return (revisionRows, clockRows, sessionRows);
                            },
                            token
                        )
                        .ConfigureAwait(false);

                    if (revisions.Length == 0 || sessions.Length == 0)
                    {
                        await transaction.RollbackAsync(token).ConfigureAwait(false);
                        return false;
                    }

                    var change = new PgKeyspaceChange(
                        Settings.Schema,
                        keyspaceId,
                        revisions[0],
                        DateTime.SpecifyKind(clocks[0], DateTimeKind.Utc),
                        Settings.LogRetention
                    );
                    foreach (var session in sessions)
                    {
                        change.EndSession(session);
                    }

                    await Table
                        .CommitAsync(transaction, change, keyspaceId, token)
                        .ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
