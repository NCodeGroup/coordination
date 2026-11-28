// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Builds the commit batch of a transaction that changes one keyspace's records: it takes the keyspace's next
/// revisions, writes each record, logs each entry in the change log, stores the last revision in the keyspace's row
/// and notifies the keyspace's channel once.
/// </summary>
/// <param name="schema">The schema that holds the tables.</param>
/// <param name="keyspaceId">The keyspace's id.</param>
/// <param name="revision">The keyspace's revision that the transaction read, which the first entry follows.</param>
/// <param name="now">The instant the transaction read on the server's clock.</param>
/// <param name="retention">How long a log entry is kept.</param>
internal sealed class PgKeyspaceChange(
    PgSchemaName schema,
    long keyspaceId,
    long revision,
    DateTime now,
    TimeSpan retention
)
{
    /// <summary>
    /// The kind that a change item holds for a record that was written.
    /// </summary>
    private const short WrittenKind = 1;

    /// <summary>
    /// The kind that a change item holds for a record that was deleted.
    /// </summary>
    private const short DeletedKind = 2;

    /// <summary>
    /// The template of the statement that writes a record, replacing the row that holds its key.
    /// </summary>
    private const string UpsertRecord = """
        INSERT INTO {schema}.records (keyspace_id, idx, depth, key, value, revision, deadline, session_id)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
        ON CONFLICT (keyspace_id, idx, depth) DO UPDATE SET
            key = EXCLUDED.key, value = EXCLUDED.value, revision = EXCLUDED.revision,
            deadline = EXCLUDED.deadline, session_id = EXCLUDED.session_id
        """;

    /// <summary>
    /// The template of the statement that deletes a record.
    /// </summary>
    private const string DeleteRecord = """
        DELETE FROM {schema}.records WHERE keyspace_id = $1 AND idx = $2 AND depth = $3
        """;

    /// <summary>
    /// The template of the statement that deletes a session's row.
    /// </summary>
    private const string DeleteSession = """
        DELETE FROM {schema}.sessions WHERE keyspace_id = $1 AND id = $2
        """;

    /// <summary>
    /// The template of the statement that logs one entry of the change log.
    /// </summary>
    private const string InsertChange = """
        INSERT INTO {schema}.changes (keyspace_id, revision, deadline, ended_session, group_idx, group_depth)
        VALUES ($1, $2, $3, $4, $5, $6)
        """;

    /// <summary>
    /// The template of the statement that logs the items of one entry.
    /// </summary>
    private const string InsertItems = """
        INSERT INTO {schema}.change_items
            (keyspace_id, revision, ordinal, kind, idx, depth, key, value, deadline, session_id, replaced_session)
        SELECT $1, $2, i.* FROM unnest(
            $3::smallint[], $4::smallint[], $5::bytea[], $6::smallint[], $7::text[],
            $8::bytea[], $9::timestamptz[], $10::text[], $11::text[]) AS i
        """;

    /// <summary>
    /// The template of the statement that stores the keyspace's last revision.
    /// </summary>
    private const string UpdateRevision = """
        UPDATE {schema}.keyspaces SET revision = $2 WHERE id = $1
        """;

    /// <summary>
    /// The template of the statement that notifies the keyspace's channel.
    /// </summary>
    private const string Notify = """
        SELECT pg_notify($1, $2)
        """;

    /// <summary>
    /// Builds an array argument that holds one element of each write.
    /// </summary>
    /// <typeparam name="T">The type of an element.</typeparam>
    /// <param name="writes">The writes, in order.</param>
    /// <param name="select">Selects the element of a write.</param>
    /// <param name="elementType">The PostgreSQL type of an element.</param>
    /// <returns>The argument, typed as an array of <paramref name="elementType"/>.</returns>
    private static PgArgument ArrayOf<T>(
        IReadOnlyList<PgRecordWrite> writes,
        Func<PgRecordWrite, T> select,
        NpgsqlDbType elementType
    )
    {
        var elements = writes.Select(select).ToArray();
        return PgArgument.Typed(elements, NpgsqlDbType.Array | elementType);
    }

    /// <summary>
    /// Gets the statement that writes a record.
    /// </summary>
    private string UpsertRecordSql { get; } = schema.Apply(UpsertRecord);

    /// <summary>
    /// Gets the statement that deletes a record.
    /// </summary>
    private string DeleteRecordSql { get; } = schema.Apply(DeleteRecord);

    /// <summary>
    /// Gets the statement that deletes a session's row.
    /// </summary>
    private string DeleteSessionSql { get; } = schema.Apply(DeleteSession);

    /// <summary>
    /// Gets the statement that logs one entry.
    /// </summary>
    private string InsertChangeSql { get; } = schema.Apply(InsertChange);

    /// <summary>
    /// Gets the statement that logs the items of one entry.
    /// </summary>
    private string InsertItemsSql { get; } = schema.Apply(InsertItems);

    /// <summary>
    /// Gets the statement that stores the keyspace's last revision.
    /// </summary>
    private string UpdateRevisionSql { get; } = schema.Apply(UpdateRevision);

    /// <summary>
    /// Gets the statement that notifies the keyspace's channel.
    /// </summary>
    private string NotifySql { get; } = schema.Apply(Notify);

    /// <summary>
    /// Gets the entries the transaction has taken, in order.
    /// </summary>
    private List<PgChangeEntry> Entries { get; } = [];

    /// <summary>
    /// Gets the sessions that this change has ended.
    /// </summary>
    private HashSet<string> EndedSessions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the writes that take no revision, and the deletions of ended sessions, in order.
    /// </summary>
    private List<PgStatement> Extras { get; } = [];

    /// <summary>
    /// Gets the keyspace's revision so far: the revision read, then the last one taken.
    /// </summary>
    public long Revision { get; private set; } = revision;

    /// <summary>
    /// Gets a value that tells whether the transaction has taken no revision and made no write, so it has nothing to
    /// commit.
    /// </summary>
    public bool IsEmpty => Entries.Count == 0 && Extras.Count == 0;

    /// <summary>
    /// Gets the instant the transaction read on the server's clock.
    /// </summary>
    public DateTime Now { get; } = now;

    /// <summary>
    /// Deletes rows that expired and logs one entry that names each of them as deleted, taking one revision. Takes
    /// none when there are no rows.
    /// </summary>
    /// <param name="rows">The expired rows.</param>
    /// <exception cref="ArgumentNullException">The rows are null.</exception>
    public void Expire(IReadOnlyList<PgRecordRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return;
        }

        Begin();
        foreach (var row in rows)
        {
            Delete(row);
        }
    }

    /// <summary>
    /// Takes the keyspace's next revision for the operation's own entry.
    /// </summary>
    /// <returns>The revision.</returns>
    public long Begin()
    {
        Revision++;
        var entry = new PgChangeEntry(Revision);
        Entries.Add(entry);
        return Revision;
    }

    /// <summary>
    /// Ends a session: takes the next revision for an entry that names the session as ended, and deletes the
    /// session's row. A session ends once in a change.
    /// </summary>
    /// <param name="session">The session's id.</param>
    /// <returns>True when the session was ended; false when this change has ended it already.</returns>
    /// <exception cref="ArgumentNullException">The session is null.</exception>
    public bool EndSession(string session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!EndedSessions.Add(session))
        {
            return false;
        }

        Begin();
        Entries[^1].EndedSession = session;
        var deleteSession = new PgStatement(DeleteSessionSql, keyspaceId, session);
        Extras.Add(deleteSession);
        return true;
    }

    /// <summary>
    /// Names the group that the current entry changed.
    /// </summary>
    /// <param name="index">The group's index value.</param>
    /// <param name="depth">The group's partition depth.</param>
    /// <exception cref="ArgumentNullException">The index is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Begin"/> was not called.</exception>
    public void NameGroup(byte[] index, short depth)
    {
        ArgumentNullException.ThrowIfNull(index);

        var entry = Current();
        entry.GroupIndex = index;
        entry.GroupDepth = depth;
    }

    /// <summary>
    /// Appends a write that takes no revision and logs nothing.
    /// </summary>
    /// <param name="statement">The statement that makes the write.</param>
    public void Write(PgStatement statement)
    {
        Extras.Add(statement);
    }

    /// <summary>
    /// Writes a record in the current entry and logs it as written.
    /// </summary>
    /// <param name="key">The record's key.</param>
    /// <param name="value">The record's value.</param>
    /// <param name="deadline">The instant the record expires, or null when it doesn't.</param>
    /// <param name="session">The id of the session the record is bound to, or null when it is bound to none.</param>
    /// <param name="replacedSession">The id of the ended session whose row the write replaced, or null.</param>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Begin"/> was not called.</exception>
    public void Put(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        DateTime? deadline,
        string? session,
        string? replacedSession
    )
    {
        ArgumentNullException.ThrowIfNull(key);

        var entry = Current();
        var index = PgKeys.Index(key);
        var depth = (short)key.PartitionDepth;
        var keyText = PgKeys.KeyText(key);
        var valueCopy = value.ToArray();
        var write = new PgRecordWrite(
            WrittenKind,
            index,
            depth,
            keyText,
            valueCopy,
            deadline,
            session,
            replacedSession
        );
        entry.Writes.Add(write);
    }

    /// <summary>
    /// Deletes a row in the current entry and logs it as deleted.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <exception cref="ArgumentNullException">The row is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Begin"/> was not called.</exception>
    public void Delete(PgRecordRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var entry = Current();
        var write = new PgRecordWrite(
            DeletedKind,
            row.Index,
            row.Depth,
            row.Key,
            null,
            null,
            null,
            null
        );
        entry.Writes.Add(write);
    }

    /// <summary>
    /// Builds the statements that commit the changes: for each entry its record writes, its row in the change log and
    /// its items, then the keyspace's revision and the notification.
    /// </summary>
    /// <param name="channel">The keyspace's channel.</param>
    /// <returns>The statements, in the order to send them; empty when <see cref="IsEmpty"/>.</returns>
    /// <exception cref="ArgumentNullException">The channel is null.</exception>
    public ImmutableArray<PgStatement> Statements(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (IsEmpty)
        {
            return [];
        }

        var statements = ImmutableArray.CreateBuilder<PgStatement>();
        statements.AddRange(Extras);

        if (Entries.Count == 0)
        {
            return statements.ToImmutable();
        }

        var logDeadline = Now + retention;
        var logDeadlineArgument = PgArgument.Typed(logDeadline, NpgsqlDbType.TimestampTz);

        foreach (var entry in Entries)
        {
            AddEntryStatements(statements, entry, logDeadlineArgument);
        }

        var payload = PgChannels.ChangePayload(Revision);
        var updateRevision = new PgStatement(UpdateRevisionSql, keyspaceId, Revision);
        var notify = new PgStatement(NotifySql, channel, payload);
        statements.Add(updateRevision);
        statements.Add(notify);

        return statements.ToImmutable();
    }

    /// <summary>
    /// Appends the statements of one entry: its record writes, its row in the change log and its items.
    /// </summary>
    /// <param name="statements">The statements so far.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="logDeadline">The argument that holds the deadline of the entry's row in the change log.</param>
    private void AddEntryStatements(
        ImmutableArray<PgStatement>.Builder statements,
        PgChangeEntry entry,
        PgArgument logDeadline
    )
    {
        foreach (var write in entry.Writes)
        {
            var writeStatement = WriteStatement(entry, write);
            statements.Add(writeStatement);
        }

        var changeStatement = ChangeStatement(entry, logDeadline);
        statements.Add(changeStatement);

        if (entry.Writes.Count > 0)
        {
            var itemsStatement = ItemsStatement(entry);
            statements.Add(itemsStatement);
        }
    }

    /// <summary>
    /// Builds the statement that makes one record write of an entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="write">The write.</param>
    /// <returns>The statement that writes the record, or the one that deletes it.</returns>
    private PgStatement WriteStatement(PgChangeEntry entry, PgRecordWrite write)
    {
        var index = PgArgument.Typed(write.Index, NpgsqlDbType.Bytea);
        if (write.Kind != WrittenKind)
        {
            return new PgStatement(DeleteRecordSql, keyspaceId, index, write.Depth);
        }

        var value = PgArgument.Typed(write.Value, NpgsqlDbType.Bytea);
        var deadline = PgArgument.Typed(write.Deadline, NpgsqlDbType.TimestampTz);
        var session = PgArgument.Typed(write.Session, NpgsqlDbType.Text);
        return new PgStatement(
            UpsertRecordSql,
            keyspaceId,
            index,
            write.Depth,
            write.Key,
            value,
            entry.Revision,
            deadline,
            session
        );
    }

    /// <summary>
    /// Builds the statement that logs an entry in the change log.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="logDeadline">The argument that holds the deadline of the entry's row.</param>
    /// <returns>The statement.</returns>
    private PgStatement ChangeStatement(PgChangeEntry entry, PgArgument logDeadline)
    {
        var endedSession = PgArgument.Typed(entry.EndedSession, NpgsqlDbType.Text);
        var groupIndex = PgArgument.Typed(entry.GroupIndex, NpgsqlDbType.Bytea);
        var groupDepth = PgArgument.Typed(entry.GroupDepth, NpgsqlDbType.Smallint);
        return new PgStatement(
            InsertChangeSql,
            keyspaceId,
            entry.Revision,
            logDeadline,
            endedSession,
            groupIndex,
            groupDepth
        );
    }

    /// <summary>
    /// Builds the statement that inserts an entry's items.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The statement, whose arrays hold one element for each write of the entry.</returns>
    private PgStatement ItemsStatement(PgChangeEntry entry)
    {
        var writes = entry.Writes;

        var ordinalValues = Enumerable.Range(0, writes.Count).Select(ordinal => (short)ordinal);
        var ordinalArray = ordinalValues.ToArray();
        var ordinals = PgArgument.Typed(ordinalArray, NpgsqlDbType.Array | NpgsqlDbType.Smallint);
        var kinds = ArrayOf(writes, write => write.Kind, NpgsqlDbType.Smallint);
        var indexes = ArrayOf(writes, write => write.Index, NpgsqlDbType.Bytea);
        var depths = ArrayOf(writes, write => write.Depth, NpgsqlDbType.Smallint);
        var keys = ArrayOf(writes, write => write.Key, NpgsqlDbType.Text);
        var values = ArrayOf(writes, write => write.Value, NpgsqlDbType.Bytea);
        var deadlines = ArrayOf(writes, write => write.Deadline, NpgsqlDbType.TimestampTz);
        var sessions = ArrayOf(writes, write => write.Session, NpgsqlDbType.Text);
        var replacedSessions = ArrayOf(writes, write => write.ReplacedSession, NpgsqlDbType.Text);

        return new PgStatement(
            InsertItemsSql,
            keyspaceId,
            entry.Revision,
            ordinals,
            kinds,
            indexes,
            depths,
            keys,
            values,
            deadlines,
            sessions,
            replacedSessions
        );
    }

    /// <summary>
    /// Gets the entry that the last <see cref="Begin"/> started.
    /// </summary>
    /// <returns>The entry.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Begin"/> was not called.</exception>
    private PgChangeEntry Current()
    {
        if (Entries.Count == 0)
        {
            throw new InvalidOperationException("Begin must be called before a write.");
        }

        return Entries[^1];
    }
}
