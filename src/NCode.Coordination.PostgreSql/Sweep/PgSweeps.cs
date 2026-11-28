// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NCode.Coordination.PostgreSql.Logging;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Sweep;

/// <summary>
/// The passes that delete expired rows of the provider's tables, each in bounded batches that skip the rows another
/// transaction holds.
/// </summary>
/// <param name="server">The seam to the server.</param>
/// <param name="settings">The settings, whose sweep batch size bounds every pass's statement.</param>
/// <param name="table">The record table, which commits the deletion of expired records as a change.</param>
/// <param name="logger">The logger that reports the failures the sweep ignores.</param>
/// <exception cref="ArgumentNullException">The server, the settings, the table or the logger are null.</exception>
internal sealed class PgSweeps(
    PgServer server,
    PgSettings settings,
    PgRecordTable table,
    ILogger<PgSweeps> logger
)
{
    /// <summary>
    /// The most keyspaces one sweep visits for expired records or for expired sessions; the next sweep lists again.
    /// </summary>
    private const int KeyspacesPerSweep = 100;

    /// <summary>
    /// The statement that deletes expired occurrences.
    /// </summary>
    private const string OccurrencesSql = """
        WITH deleted AS (
          DELETE FROM {schema}.occurrences
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.occurrences
            WHERE deadline <= clock_timestamp()
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes expired counters.
    /// </summary>
    private const string CountersSql = """
        WITH deleted AS (
          DELETE FROM {schema}.counters
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.counters
            WHERE deadline <= clock_timestamp()
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes expired set members.
    /// </summary>
    private const string SetMembersSql = """
        WITH deleted AS (
          DELETE FROM {schema}.set_members
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.set_members
            WHERE deadline <= clock_timestamp()
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes the rows of sets that hold no member.
    /// </summary>
    private const string SetsSql = """
        WITH deleted AS (
          DELETE FROM {schema}.sets
          WHERE ctid = ANY (ARRAY(
            SELECT h.ctid FROM {schema}.sets h
            WHERE h.deadline <= clock_timestamp() AND NOT EXISTS (
              SELECT 1 FROM {schema}.set_members m
              WHERE m.keyspace_id = h.keyspace_id AND m.idx = h.idx AND m.depth = h.depth)
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that lists the keyspaces that hold an expired record.
    /// </summary>
    private const string ExpiredKeyspacesSql = """
        SELECT DISTINCT keyspace_id FROM {schema}.records
        WHERE deadline <= clock_timestamp()
        LIMIT $1
        """;

    /// <summary>
    /// The statement that locks a keyspace's row, unless another transaction holds it, and reads its revision.
    /// </summary>
    private const string LockKeyspaceSql = """
        SELECT revision FROM {schema}.keyspaces WHERE id = $1 FOR NO KEY UPDATE SKIP LOCKED
        """;

    /// <summary>
    /// The statement that reads the server's instant.
    /// </summary>
    private const string ClockSql = "SELECT clock_timestamp()";

    /// <summary>
    /// The statement that reads a keyspace's expired records.
    /// </summary>
    private const string ExpiredRecordsSql = """
        SELECT idx, depth, key, value, revision, deadline, session_id
        FROM {schema}.records
        WHERE keyspace_id = $1 AND deadline <= clock_timestamp()
        ORDER BY idx, depth
        LIMIT $2
        """;

    /// <summary>
    /// The statement that lists the keyspaces that hold an expired session.
    /// </summary>
    private const string ExpiredSessionKeyspacesSql = """
        SELECT DISTINCT keyspace_id FROM {schema}.sessions
        WHERE deadline <= clock_timestamp()
        LIMIT $1
        """;

    /// <summary>
    /// The statement that reads a keyspace's expired sessions.
    /// </summary>
    private const string ExpiredSessionsSql = """
        SELECT id FROM {schema}.sessions
        WHERE keyspace_id = $1 AND deadline <= clock_timestamp()
        ORDER BY id
        LIMIT $2
        """;

    /// <summary>
    /// The statement that deletes the records bound to a session that ended and left the log.
    /// </summary>
    private const string BoundRecordsSql = """
        WITH due AS (
          SELECT e.keyspace_id, e.session_id
          FROM {schema}.ended_sessions e
          JOIN {schema}.keyspaces k ON k.id = e.keyspace_id
          WHERE e.revision <= k.log_removed
            AND EXISTS (
              SELECT 1 FROM {schema}.records b
              WHERE b.keyspace_id = e.keyspace_id AND b.session_id = e.session_id)
          LIMIT $1),
        deleted AS (
          DELETE FROM {schema}.records
          WHERE ctid = ANY (ARRAY(
            SELECT b.ctid FROM {schema}.records b
            JOIN due d ON d.keyspace_id = b.keyspace_id AND d.session_id = b.session_id
            LIMIT $1
            FOR UPDATE OF b SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes the members bound to a session that ended and left the log.
    /// </summary>
    private const string BoundMembersSql = """
        WITH due AS (
          SELECT e.keyspace_id, e.session_id
          FROM {schema}.ended_sessions e
          JOIN {schema}.keyspaces k ON k.id = e.keyspace_id
          WHERE e.revision <= k.log_removed
            AND EXISTS (
              SELECT 1 FROM {schema}.members b
              WHERE b.keyspace_id = e.keyspace_id AND b.session_id = e.session_id)
          LIMIT $1),
        deleted AS (
          DELETE FROM {schema}.members
          WHERE ctid = ANY (ARRAY(
            SELECT b.ctid FROM {schema}.members b
            JOIN due d ON d.keyspace_id = b.keyspace_id AND d.session_id = b.session_id
            LIMIT $1
            FOR UPDATE OF b SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes the grants bound to a session that ended and left the log.
    /// </summary>
    private const string BoundGrantsSql = """
        WITH due AS (
          SELECT e.keyspace_id, e.session_id
          FROM {schema}.ended_sessions e
          JOIN {schema}.keyspaces k ON k.id = e.keyspace_id
          WHERE e.revision <= k.log_removed
            AND EXISTS (
              SELECT 1 FROM {schema}.grants b
              WHERE b.keyspace_id = e.keyspace_id AND b.session_id = e.session_id)
          LIMIT $1),
        deleted AS (
          DELETE FROM {schema}.grants
          WHERE ctid = ANY (ARRAY(
            SELECT b.ctid FROM {schema}.grants b
            JOIN due d ON d.keyspace_id = b.keyspace_id AND d.session_id = b.session_id
            LIMIT $1
            FOR UPDATE OF b SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that forgets the ended sessions whose entry left the log and that nothing is bound to.
    /// </summary>
    private const string EndedSessionsSql = """
        WITH deleted AS (
          DELETE FROM {schema}.ended_sessions
          WHERE ctid = ANY (ARRAY(
            SELECT e.ctid FROM {schema}.ended_sessions e
            JOIN {schema}.keyspaces k ON k.id = e.keyspace_id
            WHERE e.revision <= k.log_removed
              AND NOT EXISTS (SELECT 1 FROM {schema}.records r WHERE r.keyspace_id = e.keyspace_id AND r.session_id = e.session_id)
              AND NOT EXISTS (SELECT 1 FROM {schema}.members m WHERE m.keyspace_id = e.keyspace_id AND m.session_id = e.session_id)
              AND NOT EXISTS (SELECT 1 FROM {schema}.grants g WHERE g.keyspace_id = e.keyspace_id AND g.session_id = e.session_id)
            LIMIT $1
            FOR UPDATE OF e SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes grants past their deadline.
    /// </summary>
    private const string GrantsSql = """
        WITH deleted AS (
          DELETE FROM {schema}.grants
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.grants
            WHERE deadline <= clock_timestamp()
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes claims past their deadline.
    /// </summary>
    private const string ClaimsSql = """
        WITH deleted AS (
          DELETE FROM {schema}.claims
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.claims
            WHERE deadline <= clock_timestamp()
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING 1)
        SELECT count(*) FROM deleted
        """;

    /// <summary>
    /// The statement that deletes the entries of the change logs that passed their retention.
    /// </summary>
    private const string LogRetentionSql = """
        WITH removed AS (
          DELETE FROM {schema}.changes
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.changes
            WHERE deadline <= clock_timestamp()
            ORDER BY keyspace_id, revision
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING keyspace_id, revision),
        raised AS (
          UPDATE {schema}.keyspaces k
          SET log_removed = greatest(k.log_removed, m.revision)
          FROM (SELECT keyspace_id, max(revision) AS revision FROM removed GROUP BY keyspace_id) m
          WHERE k.id = m.keyspace_id)
        SELECT count(*) FROM removed
        """;

    /// <summary>
    /// The statement that deletes the messages of the topics' logs that passed their retention.
    /// </summary>
    private const string MessagesSql = """
        WITH removed AS (
          DELETE FROM {schema}.messages
          WHERE ctid = ANY (ARRAY(
            SELECT ctid FROM {schema}.messages
            WHERE deadline <= clock_timestamp()
            ORDER BY keyspace_id, idx, depth, sequence
            LIMIT $1
            FOR UPDATE SKIP LOCKED))
          RETURNING keyspace_id, idx, depth, sequence),
        raised AS (
          UPDATE {schema}.topics t
          SET removed = greatest(t.removed, m.sequence)
          FROM (SELECT keyspace_id, idx, depth, max(sequence) AS sequence FROM removed GROUP BY keyspace_id, idx, depth) m
          WHERE t.keyspace_id = m.keyspace_id AND t.idx = m.idx AND t.depth = m.depth)
        SELECT count(*) FROM removed
        """;

    /// <summary>
    /// Reads the first column of the reader's current row as a 64-bit integer.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The integer.</returns>
    private static long ReadFirstInt64(NpgsqlDataReader reader)
    {
        return reader.GetInt64(0);
    }

    /// <summary>
    /// Reads the first column of the reader's current row as a time stamp.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The time stamp.</returns>
    private static DateTime ReadFirstDateTime(NpgsqlDataReader reader)
    {
        return reader.GetDateTime(0);
    }

    /// <summary>
    /// Reads the first column of the reader's current row as text.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The text.</returns>
    private static string ReadFirstText(NpgsqlDataReader reader)
    {
        return reader.GetString(0);
    }

    /// <summary>
    /// Reads an expired record from the reader's current row of seven columns: the index value, the depth, the key's
    /// text, the value, the revision, the deadline and the session's id.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The row.</returns>
    private static PgRecordRow ReadExpiredRecord(NpgsqlDataReader reader)
    {
        var index = reader.GetFieldValue<byte[]>(0);
        var depth = reader.GetInt16(1);
        var key = reader.GetString(2);
        var value = reader.GetFieldValue<byte[]>(3);
        var revision = reader.GetInt64(4);
        var deadline = PgColumns.ReadNullableUtc(reader, 5);
        var session = reader.IsDBNull(6) ? null : reader.GetString(6);

        return new PgRecordRow(index, depth, key, value, revision, deadline, session);
    }

    /// <summary>
    /// Reads the ids of the keyspaces that a listing statement returned.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The keyspaces' ids.</returns>
    private static ValueTask<ImmutableArray<long>> ReadKeyspaceIdsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return results.ReadAsync(ReadFirstInt64, cancellationToken);
    }

    /// <summary>
    /// Reads the count that a pass's statement returns: how many rows it deleted.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The number of rows the statement deleted.</returns>
    private static async ValueTask<long> ReadDeletedAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var counts = await results
            .ReadAsync(ReadFirstInt64, cancellationToken)
            .ConfigureAwait(false);

        return counts[0];
    }

    /// <summary>
    /// Reads the results of the batch that locks a keyspace and reads its expired records: the revision, the
    /// server's instant and the records.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The revision rows, the clock rows and the expired records.</returns>
    private static async ValueTask<(
        ImmutableArray<long> Revisions,
        ImmutableArray<DateTime> Clocks,
        ImmutableArray<PgRecordRow> Rows
    )> ReadExpiredRecordsAsync(PgResults results, CancellationToken cancellationToken)
    {
        var revisions = await results
            .ReadAsync(ReadFirstInt64, cancellationToken)
            .ConfigureAwait(false);
        var clocks = await results
            .ReadAsync(ReadFirstDateTime, cancellationToken)
            .ConfigureAwait(false);
        var rows = await results
            .ReadAsync(ReadExpiredRecord, cancellationToken)
            .ConfigureAwait(false);

        return (revisions, clocks, rows);
    }

    /// <summary>
    /// Reads the results of the batch that locks a keyspace and reads its expired sessions: the revision, the
    /// server's instant and the sessions' ids.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The revision rows, the clock rows and the expired sessions' ids.</returns>
    private static async ValueTask<(
        ImmutableArray<long> Revisions,
        ImmutableArray<DateTime> Clocks,
        ImmutableArray<string> Sessions
    )> ReadExpiredSessionsAsync(PgResults results, CancellationToken cancellationToken)
    {
        var revisions = await results
            .ReadAsync(ReadFirstInt64, cancellationToken)
            .ConfigureAwait(false);
        var clocks = await results
            .ReadAsync(ReadFirstDateTime, cancellationToken)
            .ConfigureAwait(false);
        var sessions = await results
            .ReadAsync(ReadFirstText, cancellationToken)
            .ConfigureAwait(false);

        return (revisions, clocks, sessions);
    }

    /// <summary>
    /// Creates a pass from its statement's template.
    /// </summary>
    /// <param name="name">The name that a log entry gives the pass.</param>
    /// <param name="template">The statement, with <c>{schema}</c> where the schema's quoted name goes.</param>
    /// <param name="schema">The schema whose quoted name the statement takes.</param>
    /// <returns>The pass.</returns>
    private static PgSweepPass CreatePass(string name, string template, PgSchemaName schema)
    {
        var sql = schema.Apply(template);

        return new PgSweepPass(name, sql);
    }

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
    /// Gets the logger that reports the failures the sweep ignores.
    /// </summary>
    private ILogger<PgSweeps> Logger { get; } =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Gets the passes that delete rows without a change, in the order they run.
    /// </summary>
    private PgSweepPass[] Passes { get; } =
    [
        CreatePass("occurrences", OccurrencesSql, settings.Schema),
        CreatePass("counters", CountersSql, settings.Schema),
        CreatePass("set members", SetMembersSql, settings.Schema),
        CreatePass("sets", SetsSql, settings.Schema),
    ];

    /// <summary>
    /// Gets the passes that run after the sessions ended, in the order they run.
    /// </summary>
    private PgSweepPass[] PassesAfterSessions { get; } =
    [
        CreatePass("bound records", BoundRecordsSql, settings.Schema),
        CreatePass("bound members", BoundMembersSql, settings.Schema),
        CreatePass("bound grants", BoundGrantsSql, settings.Schema),
        CreatePass("ended sessions", EndedSessionsSql, settings.Schema),
        CreatePass("grants", GrantsSql, settings.Schema),
        CreatePass("claims", ClaimsSql, settings.Schema),
    ];

    /// <summary>
    /// Gets the statement that lists the keyspaces that hold an expired session.
    /// </summary>
    private string ExpiredSessionKeyspaces { get; } =
        settings.Schema.Apply(ExpiredSessionKeyspacesSql);

    /// <summary>
    /// Gets the statement that reads a keyspace's expired sessions.
    /// </summary>
    private string ExpiredSessions { get; } = settings.Schema.Apply(ExpiredSessionsSql);

    /// <summary>
    /// Gets the statement that lists the keyspaces that hold an expired record.
    /// </summary>
    private string ExpiredKeyspaces { get; } = settings.Schema.Apply(ExpiredKeyspacesSql);

    /// <summary>
    /// Gets the statement that locks a keyspace's row, unless another transaction holds it.
    /// </summary>
    private string LockKeyspace { get; } = settings.Schema.Apply(LockKeyspaceSql);

    /// <summary>
    /// Gets the statement that reads a keyspace's expired records.
    /// </summary>
    private string ExpiredRecords { get; } = settings.Schema.Apply(ExpiredRecordsSql);

    /// <summary>
    /// Gets the pass that deletes the change log entries that passed their retention.
    /// </summary>
    private PgSweepPass LogRetention { get; } =
        CreatePass("change log", LogRetentionSql, settings.Schema);

    /// <summary>
    /// Gets the pass that deletes the messages that passed their retention.
    /// </summary>
    private PgSweepPass Messages { get; } = CreatePass("messages", MessagesSql, settings.Schema);

    /// <summary>
    /// Runs each pass, in order, each repeated while it deletes a full batch. A pass that fails doesn't stop the passes
    /// after it.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after every pass ran.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask SweepAsync(CancellationToken cancellationToken)
    {
        foreach (var pass in Passes)
        {
            await RunPassAsync(pass, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await IgnoreFailureAsync("records", SweepRecordsAsync, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await IgnoreFailureAsync("sessions", SweepSessionsAsync, cancellationToken)
            .ConfigureAwait(false);

        foreach (var pass in PassesAfterSessions)
        {
            await RunPassAsync(pass, cancellationToken).ConfigureAwait(false);
        }

        await RunPassAsync(LogRetention, cancellationToken).ConfigureAwait(false);
        await RunPassAsync(Messages, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one pass that is a single statement, again while it deletes a full batch, and ignores its failure.
    /// </summary>
    /// <param name="pass">The pass, whose statement's only argument is the batch size and which returns the count of rows it deleted.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the pass ran.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    internal async ValueTask RunPassAsync(PgSweepPass pass, CancellationToken cancellationToken)
    {
        var statement = new PgStatement(pass.Sql, Settings.SweepBatchSize);
        try
        {
            long deleted;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                deleted = await Server
                    .ExecuteAsync([statement], ReadDeletedAsync, cancellationToken)
                    .ConfigureAwait(false);
            } while (deleted == Settings.SweepBatchSize);
        }
        catch (CoordinationException exception)
        {
            // The next sweep runs the pass again, and one failing pass must not stop the others.
            Logger.SweepPassFailed(exception, pass.Name);
        }
        catch (PgKeyspaceMissingException exception)
        {
            // The next sweep runs the pass again, and one failing pass must not stop the others.
            Logger.SweepPassFailed(exception, pass.Name);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // An unexpected failure of one pass (or step) must not stop the others; the next sweep runs it again.
            Logger.SweepPassFaulted(exception, pass.Name);
        }
    }

    /// <summary>
    /// Runs one step of a sweep and logs and ignores its failure.
    /// </summary>
    /// <param name="step">The name that a log entry gives the step.</param>
    /// <param name="run">The step to run.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the step ran or failed.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    internal async ValueTask IgnoreFailureAsync(
        string step,
        Func<CancellationToken, ValueTask> run,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await run(cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationException exception)
        {
            // The next sweep reads the rows again, and one failing step must not stop the others.
            Logger.SweepStepFailed(exception, step);
        }
        catch (PgKeyspaceMissingException exception)
        {
            // The next sweep reads the rows again, and one failing step must not stop the others.
            Logger.SweepStepFailed(exception, step);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // An unexpected failure of one pass (or step) must not stop the others; the next sweep runs it again.
            Logger.SweepStepFaulted(exception, step);
        }
    }

    /// <summary>
    /// Runs a batch of a keyspace's sweep again while it expired or ended a full batch.
    /// </summary>
    /// <param name="sweepBatch">Sweeps one batch and returns how many rows it expired or ended.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after a batch was not full.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    private async ValueTask RepeatWhileFullAsync(
        Func<CancellationToken, ValueTask<int>> sweepBatch,
        CancellationToken cancellationToken
    )
    {
        int swept;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            swept = await sweepBatch(cancellationToken).ConfigureAwait(false);
        } while (swept == Settings.SweepBatchSize);
    }

    /// <summary>
    /// Deletes expired records, batches for each keyspace that holds some, and logs each batch as one entry. A
    /// keyspace whose row another transaction holds is skipped, and so is one whose records no longer are expired.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after every keyspace was visited.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask SweepRecordsAsync(CancellationToken cancellationToken)
    {
        var keyspaceIds = await ListKeyspacesAsync(ExpiredKeyspaces, cancellationToken)
            .ConfigureAwait(false);

        foreach (var keyspaceId in keyspaceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await IgnoreFailureAsync(
                    "a keyspace's records",
                    token => SweepKeyspaceFullyAsync(keyspaceId, token),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes one keyspace's expired records in batches, until a batch is not full.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the last batch was committed, or skipped.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private ValueTask SweepKeyspaceFullyAsync(long keyspaceId, CancellationToken cancellationToken)
    {
        return RepeatWhileFullAsync(
            token => SweepKeyspaceAsync(keyspaceId, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Lists the keyspaces that a listing statement names.
    /// </summary>
    /// <param name="sql">The listing statement, whose one argument is the most keyspaces to list.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>The keyspaces' ids.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<ImmutableArray<long>> ListKeyspacesAsync(
        string sql,
        CancellationToken cancellationToken
    )
    {
        var statement = new PgStatement(sql, KeyspacesPerSweep);
        return await Server
            .ExecuteAsync([statement], ReadKeyspaceIdsAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a batch of one keyspace's expired records in a transaction that holds the keyspace's row.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>The number of records the batch expired; 0 when it was skipped.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private ValueTask<int> SweepKeyspaceAsync(long keyspaceId, CancellationToken cancellationToken)
    {
        return Server.TransactAsync(
            PgIsolation.ReadCommitted,
            (transaction, token) => ExpireRecordsAsync(transaction, keyspaceId, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Locks a keyspace's row, reads its expired records and commits their deletion as one change, or rolls back when
    /// the row is held or no record is expired.
    /// </summary>
    /// <param name="transaction">The transaction, which has not begun.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>The number of records the batch expired; 0 when it was skipped.</returns>
    private async ValueTask<int> ExpireRecordsAsync(
        PgTransaction transaction,
        long keyspaceId,
        CancellationToken cancellationToken
    )
    {
        var lockStatement = new PgStatement(LockKeyspace, keyspaceId);
        var clockStatement = new PgStatement(ClockSql);
        var recordsStatement = new PgStatement(ExpiredRecords, keyspaceId, Settings.SweepBatchSize);
        var (revisions, clocks, rows) = await transaction
            .FirstAsync(
                [lockStatement, clockStatement, recordsStatement],
                ReadExpiredRecordsAsync,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (revisions.Length == 0 || rows.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var change = CreateChange(keyspaceId, revisions[0], clocks[0]);
        change.Expire(rows);

        await Table
            .CommitAsync(transaction, change, keyspaceId, cancellationToken)
            .ConfigureAwait(false);
        return rows.Length;
    }

    /// <summary>
    /// Creates the change that holds a sweep's deletions in a keyspace.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="revision">The keyspace's revision.</param>
    /// <param name="clock">The server's instant.</param>
    /// <returns>The change, which logs for the configured retention.</returns>
    private PgKeyspaceChange CreateChange(long keyspaceId, long revision, DateTime clock)
    {
        var now = DateTime.SpecifyKind(clock, DateTimeKind.Utc);
        var origin = new PgChangeOrigin(keyspaceId, revision, now);
        return new PgKeyspaceChange(Settings, origin);
    }

    /// <summary>
    /// Ends expired sessions, batches for each keyspace that holds some, as one change for each session. A keyspace
    /// whose row another transaction holds is skipped, and so is one whose sessions no longer are expired.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after every keyspace was visited.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask SweepSessionsAsync(CancellationToken cancellationToken)
    {
        var keyspaceIds = await ListKeyspacesAsync(ExpiredSessionKeyspaces, cancellationToken)
            .ConfigureAwait(false);

        foreach (var keyspaceId in keyspaceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await IgnoreFailureAsync(
                    "a keyspace's sessions",
                    token => SweepSessionKeyspaceFullyAsync(keyspaceId, token),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ends one keyspace's expired sessions in batches, until a batch is not full.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the last batch was committed, or skipped.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private ValueTask SweepSessionKeyspaceFullyAsync(
        long keyspaceId,
        CancellationToken cancellationToken
    )
    {
        return RepeatWhileFullAsync(
            token => SweepSessionKeyspaceAsync(keyspaceId, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Ends a batch of one keyspace's expired sessions in a transaction that holds the keyspace's row.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>The number of sessions the batch ended; 0 when it was skipped.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private ValueTask<int> SweepSessionKeyspaceAsync(
        long keyspaceId,
        CancellationToken cancellationToken
    )
    {
        return Server.TransactAsync(
            PgIsolation.ReadCommitted,
            (transaction, token) => EndSessionsAsync(transaction, keyspaceId, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Locks a keyspace's row, reads its expired sessions and commits their end as one change for each, or rolls back
    /// when the row is held or no session is expired.
    /// </summary>
    /// <param name="transaction">The transaction, which has not begun.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>The number of sessions the batch ended; 0 when it was skipped.</returns>
    private async ValueTask<int> EndSessionsAsync(
        PgTransaction transaction,
        long keyspaceId,
        CancellationToken cancellationToken
    )
    {
        var lockStatement = new PgStatement(LockKeyspace, keyspaceId);
        var clockStatement = new PgStatement(ClockSql);
        var sessionsStatement = new PgStatement(
            ExpiredSessions,
            keyspaceId,
            Settings.SweepBatchSize
        );
        var (revisions, clocks, sessions) = await transaction
            .FirstAsync(
                [lockStatement, clockStatement, sessionsStatement],
                ReadExpiredSessionsAsync,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (revisions.Length == 0 || sessions.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var change = CreateChange(keyspaceId, revisions[0], clocks[0]);
        foreach (var session in sessions)
        {
            change.EndSession(session);
        }

        await Table
            .CommitAsync(transaction, change, keyspaceId, cancellationToken)
            .ConfigureAwait(false);
        return sessions.Length;
    }
}
