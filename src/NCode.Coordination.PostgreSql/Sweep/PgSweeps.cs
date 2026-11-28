// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Records;
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
/// <exception cref="ArgumentNullException">The server, the settings or the table are null.</exception>
internal sealed class PgSweeps(PgServer server, PgSettings settings, PgRecordTable table)
{
    /// <summary>
    /// The statement that deletes expired occurrences.
    /// </summary>
    private const string OccurrencesSql = """
        DELETE FROM {schema}.occurrences
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.occurrences
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>
    /// The statement that deletes expired counters.
    /// </summary>
    private const string CountersSql = """
        DELETE FROM {schema}.counters
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.counters
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>
    /// The statement that deletes expired set members.
    /// </summary>
    private const string SetMembersSql = """
        DELETE FROM {schema}.set_members
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.set_members
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>
    /// The statement that deletes the rows of sets that hold no member.
    /// </summary>
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
    /// The statement that lists the keyspaces that hold an expired record.
    /// </summary>
    private const string ExpiredKeyspacesSql = """
        SELECT DISTINCT keyspace_id FROM {schema}.records
        WHERE deadline <= clock_timestamp()
        LIMIT 100
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
        LIMIT 100
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

    /// <summary>
    /// The statement that deletes the members bound to a session that ended and left the log.
    /// </summary>
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

    /// <summary>
    /// The statement that deletes the grants bound to a session that ended and left the log.
    /// </summary>
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

    /// <summary>
    /// The statement that deletes grants past their deadline.
    /// </summary>
    private const string GrantsSql = """
        DELETE FROM {schema}.grants
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.grants
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
        """;

    /// <summary>
    /// The statement that deletes claims past their deadline.
    /// </summary>
    private const string ClaimsSql = """
        DELETE FROM {schema}.claims
        WHERE ctid = ANY (ARRAY(
          SELECT ctid FROM {schema}.claims
          WHERE deadline <= clock_timestamp()
          LIMIT $1
          FOR UPDATE SKIP LOCKED))
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
          RETURNING keyspace_id, revision)
        UPDATE {schema}.keyspaces k
        SET log_removed = greatest(k.log_removed, m.revision)
        FROM (SELECT keyspace_id, max(revision) AS revision FROM removed GROUP BY keyspace_id) m
        WHERE k.id = m.keyspace_id
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
          RETURNING keyspace_id, idx, depth, sequence)
        UPDATE {schema}.topics t
        SET removed = greatest(t.removed, m.sequence)
        FROM (SELECT keyspace_id, idx, depth, max(sequence) AS sequence FROM removed GROUP BY keyspace_id, idx, depth) m
        WHERE t.keyspace_id = m.keyspace_id AND t.idx = m.idx AND t.depth = m.depth
        """;

    /// <summary>
    /// Runs one step of a sweep and ignores its failure.
    /// </summary>
    /// <param name="step">The step to run.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>A task that completes after the step ran or failed.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    private static async ValueTask IgnoreFailureAsync(
        Func<CancellationToken, ValueTask> step,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await step(cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationException)
        {
            // The next sweep reads the rows again, and one failing step must not stop the others.
        }
        catch (PgKeyspaceMissingException)
        {
            // The next sweep reads the rows again, and one failing step must not stop the others.
        }
    }

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
    /// Reads a time stamp column that can be null as an instant on the server's clock.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The instant, as UTC, or null when the column is null.</returns>
    private static DateTime? ReadNullableUtc(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetDateTime(ordinal);
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
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
        var deadline = ReadNullableUtc(reader, 5);
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
    /// Gets the statement that deletes the change log entries that passed their retention.
    /// </summary>
    private string LogRetention { get; } = settings.Schema.Apply(LogRetentionSql);

    /// <summary>
    /// Gets the statement that deletes the messages that passed their retention.
    /// </summary>
    private string Messages { get; } = settings.Schema.Apply(MessagesSql);

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
        await IgnoreFailureAsync(SweepRecordsAsync, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        await IgnoreFailureAsync(SweepSessionsAsync, cancellationToken).ConfigureAwait(false);

        foreach (var sql in PassesAfterSessions)
        {
            await RunPassAsync(sql, cancellationToken).ConfigureAwait(false);
        }

        await RunPassAsync(LogRetention, cancellationToken).ConfigureAwait(false);
        await RunPassAsync(Messages, cancellationToken).ConfigureAwait(false);
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

        var statement = new PgStatement(sql, Settings.SweepBatchSize);
        try
        {
            await Server
                .ExecuteAsync(
                    [statement],
                    static (_, _) => ValueTask.FromResult(true),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (CoordinationException)
        {
            // The next sweep runs the pass again, and one failing pass must not stop the others.
        }
        catch (PgKeyspaceMissingException)
        {
            // The next sweep runs the pass again, and one failing pass must not stop the others.
        }
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
        var keyspaceIds = await ListKeyspacesAsync(ExpiredKeyspaces, cancellationToken)
            .ConfigureAwait(false);

        foreach (var keyspaceId in keyspaceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await IgnoreFailureAsync(
                    token => SweepKeyspaceAsync(keyspaceId, token),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists the keyspaces that a listing statement names.
    /// </summary>
    /// <param name="sql">The listing statement, which takes no argument.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns>The keyspaces' ids.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<ImmutableArray<long>> ListKeyspacesAsync(
        string sql,
        CancellationToken cancellationToken
    )
    {
        var statement = new PgStatement(sql);
        return await Server
            .ExecuteAsync([statement], ReadKeyspaceIdsAsync, cancellationToken)
            .ConfigureAwait(false);
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
                (transaction, token) => ExpireRecordsAsync(transaction, keyspaceId, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Locks a keyspace's row, reads its expired records and commits their deletion as one change, or rolls back when
    /// the row is held or no record is expired.
    /// </summary>
    /// <param name="transaction">The transaction, which has not begun.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns><see langword="true"/> when records were deleted; otherwise, <see langword="false"/>.</returns>
    private async ValueTask<bool> ExpireRecordsAsync(
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
            return false;
        }

        var change = CreateChange(keyspaceId, revisions[0], clocks[0]);
        change.Expire(rows);

        await Table
            .CommitAsync(transaction, change, keyspaceId, cancellationToken)
            .ConfigureAwait(false);
        return true;
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
        return new PgKeyspaceChange(
            Settings.Schema,
            keyspaceId,
            revision,
            now,
            Settings.LogRetention
        );
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
        var keyspaceIds = await ListKeyspacesAsync(ExpiredSessionKeyspaces, cancellationToken)
            .ConfigureAwait(false);

        foreach (var keyspaceId in keyspaceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await IgnoreFailureAsync(
                    token => SweepSessionKeyspaceAsync(keyspaceId, token),
                    cancellationToken
                )
                .ConfigureAwait(false);
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
                (transaction, token) => EndSessionsAsync(transaction, keyspaceId, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Locks a keyspace's row, reads its expired sessions and commits their end as one change for each, or rolls back
    /// when the row is held or no session is expired.
    /// </summary>
    /// <param name="transaction">The transaction, which has not begun.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="cancellationToken">The token that cancels the sweep.</param>
    /// <returns><see langword="true"/> when sessions were ended; otherwise, <see langword="false"/>.</returns>
    private async ValueTask<bool> EndSessionsAsync(
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
            return false;
        }

        var change = CreateChange(keyspaceId, revisions[0], clocks[0]);
        foreach (var session in sessions)
        {
            change.EndSession(session);
        }

        await Table
            .CommitAsync(transaction, change, keyspaceId, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
