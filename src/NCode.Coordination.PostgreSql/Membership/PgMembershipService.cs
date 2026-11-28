// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// The membership groups of the PostgreSQL provider: each member is a row of the <c>members</c> table bound to its
/// session, so it is absent once its session ends. A join and a leave are transactions that lock the keyspace's row,
/// end the expired sessions they meet and log one entry that names the group. A read is one statement, and a watch
/// subscribes to the keyspace's change log and reads the group again after a resync and after each entry that names
/// the group or ends a session, yielding the view when its revision is above the last one yielded.
/// </summary>
/// <param name="table">The record table, whose keyspaces' ids and transactions the service shares.</param>
/// <param name="changeLog">The reader of a keyspace's change log.</param>
/// <param name="hub">The owner of the provider's listening connection and logs' fanouts.</param>
/// <exception cref="ArgumentNullException">An argument is null.</exception>
internal sealed class PgMembershipService(PgRecordTable table, PgChangeLog changeLog, PgLogHub hub)
    : IMembershipService
{
    /// <summary>The template of the statement that reads a group's rows beside the deadlines of their sessions.</summary>
    private const string SelectGroup = """
        SELECT m.session_id, m.node_id, s.deadline
        FROM {schema}.members m
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = m.keyspace_id AND s.id = m.session_id
        WHERE m.keyspace_id = $1 AND m.group_idx = $2 AND m.group_depth = $3
        """;

    /// <summary>The template of the statement that reads the deadline of a session's row.</summary>
    private const string SelectSession = """
        SELECT deadline FROM {schema}.sessions WHERE keyspace_id = $1 AND id = $2
        """;

    /// <summary>The template of the statement that adds a member to a group, replacing its session's row.</summary>
    private const string UpsertMember = """
        INSERT INTO {schema}.members
            (keyspace_id, group_idx, group_depth, group_key, session_id, node_id, address, metadata_names, metadata_values)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
        ON CONFLICT (keyspace_id, group_idx, group_depth, session_id) DO UPDATE SET
            group_key = EXCLUDED.group_key, node_id = EXCLUDED.node_id, address = EXCLUDED.address,
            metadata_names = EXCLUDED.metadata_names, metadata_values = EXCLUDED.metadata_values
        """;

    /// <summary>The template of the statement that removes a session's member from a group.</summary>
    private const string DeleteMember = """
        DELETE FROM {schema}.members
        WHERE keyspace_id = $1 AND group_idx = $2 AND group_depth = $3 AND session_id = $4
        """;

    /// <summary>The template of the statement that reads a group's live members at the keyspace's revision.</summary>
    private const string SelectMembers = """
        WITH now AS (SELECT clock_timestamp() AS t)
        SELECT k.revision, now.t, m.session_id, m.node_id, m.address, m.metadata_names, m.metadata_values, s.deadline
        FROM now
        CROSS JOIN {schema}.keyspaces k
        LEFT JOIN ({schema}.members m JOIN {schema}.sessions s ON s.keyspace_id = m.keyspace_id AND s.id = m.session_id)
            ON m.keyspace_id = k.id AND m.group_idx = $2 AND m.group_depth = $3
        WHERE k.id = $1
        ORDER BY m.node_id
        """;

    /// <summary>The template of the statement that waits for the writers of a keyspace's row.</summary>
    private const string LockShare = """
        SELECT 1 FROM {schema}.keyspaces WHERE id = $1 FOR SHARE
        """;

    /// <summary>
    /// Gets the record table, whose keyspaces' ids and transactions the service shares.
    /// </summary>
    private PgRecordTable Table { get; } = table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the reader of a keyspace's change log.
    /// </summary>
    private PgChangeLog ChangeLog { get; } =
        changeLog ?? throw new ArgumentNullException(nameof(changeLog));

    /// <summary>
    /// Gets the owner of the provider's listening connection and logs' fanouts.
    /// </summary>
    private PgLogHub Hub { get; } = hub ?? throw new ArgumentNullException(nameof(hub));

    /// <summary>Gets the statement that reads a group's rows beside the deadlines of their sessions.</summary>
    private string SelectGroupSql { get; } = table.Settings.Schema.Apply(SelectGroup);

    /// <summary>Gets the statement that reads the deadline of a session's row.</summary>
    private string SelectSessionSql { get; } = table.Settings.Schema.Apply(SelectSession);

    /// <summary>Gets the statement that adds a member to a group.</summary>
    private string UpsertMemberSql { get; } = table.Settings.Schema.Apply(UpsertMember);

    /// <summary>Gets the statement that removes a session's member from a group.</summary>
    private string DeleteMemberSql { get; } = table.Settings.Schema.Apply(DeleteMember);

    /// <summary>Gets the statement that reads a group's live members.</summary>
    private string SelectMembersSql { get; } = table.Settings.Schema.Apply(SelectMembers);

    /// <summary>Gets the statement that waits for the writers of a keyspace's row.</summary>
    private string LockShareSql { get; } = table.Settings.Schema.Apply(LockShare);

    /// <inheritdoc />
    public async ValueTask<MembershipJoinStatus> JoinAsync(
        CoordinationKey group,
        Member member,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(member);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(group);
        var depth = (short)group.PartitionDepth;
        var sessionId = member.Session.Value;
        var ordered = member.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var names = ordered.Select(pair => pair.Key).ToArray();
        var values = ordered.Select(pair => pair.Value).ToArray();

        return await Table
            .Keyspaces.RunAsync<MembershipJoinStatus>(
                group.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await BeginAsync(
                                        transaction,
                                        id,
                                        index,
                                        depth,
                                        sessionId,
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);

                                start.EndExpiredSessions();

                                var now = start.Change.Now;
                                MembershipJoinStatus status;
                                if (start.SessionDeadline is not { } deadline || deadline <= now)
                                {
                                    status = MembershipJoinStatus.SessionEnded;
                                }
                                else if (
                                    start.Rows.Any(row =>
                                        row.IsLiveAt(now)
                                        && string.Equals(
                                            row.NodeId,
                                            member.NodeId,
                                            StringComparison.Ordinal
                                        )
                                        && !string.Equals(
                                            row.Session,
                                            sessionId,
                                            StringComparison.Ordinal
                                        )
                                    )
                                )
                                {
                                    status = MembershipJoinStatus.NodeIdTaken;
                                }
                                else
                                {
                                    start.Change.Begin();
                                    start.Change.NameGroup(index, depth);
                                    start.Change.Write(
                                        new PgStatement(
                                            UpsertMemberSql,
                                            PgArgument.Of(id),
                                            PgArgument.Typed(index, NpgsqlDbType.Bytea),
                                            PgArgument.Of(depth),
                                            PgArgument.Of(PgKeys.KeyText(group)),
                                            PgArgument.Of(sessionId),
                                            PgArgument.Of(member.NodeId),
                                            PgArgument.Of(member.Address),
                                            PgArgument.Typed(
                                                names,
                                                NpgsqlDbType.Array | NpgsqlDbType.Text
                                            ),
                                            PgArgument.Typed(
                                                values,
                                                NpgsqlDbType.Array | NpgsqlDbType.Text
                                            )
                                        )
                                    );
                                    status = MembershipJoinStatus.Joined;
                                }

                                await Table
                                    .CommitAsync(transaction, start.Change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return status;
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask LeaveAsync(
        CoordinationKey group,
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(session);
        PgCall.Check(options, cancellationToken);

        var index = PgKeys.Index(group);
        var depth = (short)group.PartitionDepth;

        await Table
            .Keyspaces.RunAsync(
                group.Keyspace,
                async (id, token) =>
                    await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                            {
                                var start = await BeginAsync(
                                        transaction,
                                        id,
                                        index,
                                        depth,
                                        session.Value,
                                        transactionToken
                                    )
                                    .ConfigureAwait(false);

                                start.EndExpiredSessions();

                                var now = start.Change.Now;
                                if (
                                    start.Rows.Any(row =>
                                        row.IsLiveAt(now)
                                        && string.Equals(
                                            row.Session,
                                            session.Value,
                                            StringComparison.Ordinal
                                        )
                                    )
                                )
                                {
                                    start.Change.Begin();
                                    start.Change.NameGroup(index, depth);
                                    start.Change.Write(
                                        new PgStatement(
                                            DeleteMemberSql,
                                            PgArgument.Of(id),
                                            PgArgument.Typed(index, NpgsqlDbType.Bytea),
                                            PgArgument.Of(depth),
                                            PgArgument.Of(session.Value)
                                        )
                                    );
                                }

                                await Table
                                    .CommitAsync(transaction, start.Change, id, transactionToken)
                                    .ConfigureAwait(false);
                                return true;
                            },
                            token
                        )
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<MembershipView> ReadAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        PgCall.Check(options, cancellationToken);

        return await ReadViewAsync(group, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<MembershipView> WatchAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        PgCall.Check(options, cancellationToken);

        return WatchCoreAsync(group, cancellationToken);
    }

    /// <summary>
    /// Streams the views of a watch: the group at the start, then a view each time that a read, which it makes after a
    /// resync and after each entry of the change log that names the group or ends a session, finds a revision above
    /// that of the last view yielded.
    /// </summary>
    /// <param name="group">The group to watch.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of views.</returns>
    private async IAsyncEnumerable<MembershipView> WatchCoreAsync(
        CoordinationKey group,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var keyspaceId = await Table
            .Keyspaces.ResolveAsync(group.Keyspace, cancellationToken)
            .ConfigureAwait(false);
        var index = PgKeys.Index(group);
        var depth = (short)group.PartitionDepth;

        var logId = new PgLogId(keyspaceId, null);
        var subscriber = await Hub.SubscribeAsync(
                logId,
                () =>
                    new PgLogFanout<PgChange>(
                        (cursor, limit, readToken) =>
                            ChangeLog.ReadAsync(keyspaceId, cursor, limit, readToken)
                    ),
                null,
                cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
            var last = await ReadViewAsync(group, cancellationToken).ConfigureAwait(false);
            yield return last;

            await foreach (
                var item in subscriber.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                if (!item.IsResync && !Concerns(item.Entry, index, depth))
                {
                    continue;
                }

                var view = await ReadViewAsync(group, cancellationToken).ConfigureAwait(false);
                if (view.Revision.Value > last.Revision.Value)
                {
                    last = view;
                    yield return view;
                }
            }
        }
        finally
        {
            await Hub.UnsubscribeAsync(logId, subscriber).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells whether an entry of the change log may change a group's members: it names the group or ends a session.
    /// </summary>
    /// <param name="entry">The entry, or null.</param>
    /// <param name="index">The group's index value.</param>
    /// <param name="depth">The group's partition depth.</param>
    /// <returns><see langword="true"/> when the group's view may have changed.</returns>
    private static bool Concerns(PgChange? entry, byte[] index, short depth) =>
        entry is not null
        && (
            entry.EndedSession is not null
            || (
                entry.GroupDepth == depth
                && entry.GroupIndex is { } groupIndex
                && groupIndex.AsSpan().SequenceEqual(index)
            )
        );

    /// <summary>
    /// Reads a group's live members at the keyspace's revision. A member whose session's deadline has passed makes the
    /// read run again after it waits for the keyspace's writers, so the view follows the write that ends the session.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The view.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<MembershipView> ReadViewAsync(
        CoordinationKey group,
        CancellationToken cancellationToken
    )
    {
        var index = PgKeys.Index(group);
        var depth = (short)group.PartitionDepth;

        return await Table
            .Keyspaces.RunAsync<MembershipView>(
                group.Keyspace,
                async (id, token) =>
                {
                    var first = await Table
                        .Server.ExecuteAsync(
                            [SelectMembersStatement(id, index, depth)],
                            ReadMembersAsync,
                            token
                        )
                        .ConfigureAwait(false);

                    if (first.Length == 0)
                    {
                        throw new PgKeyspaceMissingException("The keyspace's row does not exist.");
                    }

                    if (!first.Any(row => row.IsExpired()))
                    {
                        return ToView(group, first);
                    }

                    var second = await Table
                        .Server.TransactAsync(
                            PgIsolation.ReadCommitted,
                            async (transaction, transactionToken) =>
                                await transaction
                                    .FirstAsync(
                                        [
                                            new PgStatement(LockShareSql, PgArgument.Of(id)),
                                            SelectMembersStatement(id, index, depth),
                                        ],
                                        async (results, readToken) =>
                                        {
                                            await results
                                                .ReadAsync(reader => reader.GetInt32(0), readToken)
                                                .ConfigureAwait(false);
                                            return await ReadMembersAsync(results, readToken)
                                                .ConfigureAwait(false);
                                        },
                                        transactionToken
                                    )
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false);

                    if (second.Length == 0)
                    {
                        throw new PgKeyspaceMissingException("The keyspace's row does not exist.");
                    }

                    return ToView(group, second);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the statement that reads a group's members.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="index">The group's index value.</param>
    /// <param name="depth">The group's partition depth.</param>
    /// <returns>The statement.</returns>
    private PgStatement SelectMembersStatement(long keyspaceId, byte[] index, short depth) =>
        new(
            SelectMembersSql,
            PgArgument.Of(keyspaceId),
            PgArgument.Typed(index, NpgsqlDbType.Bytea),
            PgArgument.Of(depth)
        );

    /// <summary>
    /// Reads the result of the statement that lists a group's members.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The rows; none when the keyspace's row does not exist.</returns>
    private static async ValueTask<ImmutableArray<MemberRow>> ReadMembersAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results
            .ReadAsync(
                reader => new MemberRow(
                    reader.GetInt64(0),
                    DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<string[]>(5),
                    reader.IsDBNull(6) ? null : reader.GetFieldValue<string[]>(6),
                    reader.IsDBNull(7)
                        ? null
                        : DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the view of a group from the rows that were read, keeping the live members only.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="rows">The rows, which are not empty.</param>
    /// <returns>The view at the keyspace's revision.</returns>
    private static MembershipView ToView(CoordinationKey group, ImmutableArray<MemberRow> rows)
    {
        var members = new List<Member>();
        foreach (var row in rows)
        {
            if (
                row
                    is not {
                        Session: { } session,
                        NodeId: { } nodeId,
                        Address: { } address,
                        Names: { } names,
                        Values: { } values,
                        Deadline: { } deadline,
                    }
                || deadline <= row.Now
            )
            {
                continue;
            }

            var metadata = ImmutableDictionary.CreateRange(
                names.Zip(values, (name, value) => KeyValuePair.Create(name, value))
            );
            members.Add(
                new Member(new SessionId(group.Keyspace, session), nodeId, address)
                {
                    Metadata = metadata,
                }
            );
        }

        return new MembershipView(group, new Revision(rows[0].Revision), members);
    }

    /// <summary>
    /// Reads what the first batch of a join or a leave needs: it locks the keyspace's row, reads the server's instant,
    /// the group's rows with the deadlines of their sessions, and the deadline of the asked session's row.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="index">The group's index value.</param>
    /// <param name="depth">The group's partition depth.</param>
    /// <param name="session">The id of the session that joins or leaves.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What the batch read.</returns>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    private async ValueTask<Start> BeginAsync(
        PgTransaction transaction,
        long keyspaceId,
        byte[] index,
        short depth,
        string session,
        CancellationToken cancellationToken
    )
    {
        var (revisions, clocks, rows, deadlines) = await transaction
            .FirstAsync(
                [
                    Table.LockKeyspace(keyspaceId),
                    Table.Clock(),
                    new PgStatement(
                        SelectGroupSql,
                        PgArgument.Of(keyspaceId),
                        PgArgument.Typed(index, NpgsqlDbType.Bytea),
                        PgArgument.Of(depth)
                    ),
                    new PgStatement(
                        SelectSessionSql,
                        PgArgument.Of(keyspaceId),
                        PgArgument.Of(session)
                    ),
                ],
                async (results, token) =>
                {
                    var revisionRows = await results
                        .ReadAsync(reader => reader.GetInt64(0), token)
                        .ConfigureAwait(false);
                    var clockRows = await results
                        .ReadAsync(reader => reader.GetDateTime(0), token)
                        .ConfigureAwait(false);
                    var groupRows = await results
                        .ReadAsync(
                            reader => new GroupRow(
                                reader.GetString(0),
                                reader.GetString(1),
                                reader.IsDBNull(2)
                                    ? null
                                    : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)
                            ),
                            token
                        )
                        .ConfigureAwait(false);
                    var sessionRows = await results
                        .ReadAsync(
                            reader => DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc),
                            token
                        )
                        .ConfigureAwait(false);

                    return (revisionRows, clockRows, groupRows, sessionRows);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (revisions.Length == 0)
        {
            throw new PgKeyspaceMissingException("The keyspace's row does not exist.");
        }

        var now = DateTime.SpecifyKind(clocks[0], DateTimeKind.Utc);
        var change = Table.NewChange(keyspaceId, revisions[0], now);

        return new Start(change, session, rows, deadlines.Length == 0 ? null : deadlines[0]);
    }

    /// <summary>
    /// Holds what the first batch of a join or a leave read.
    /// </summary>
    /// <param name="Change">The change that the transaction builds.</param>
    /// <param name="Session">The id of the session that joins or leaves.</param>
    /// <param name="Rows">The group's rows.</param>
    /// <param name="SessionDeadline">The deadline of the session's row, or null when it does not exist.</param>
    private sealed record Start(
        PgKeyspaceChange Change,
        string Session,
        ImmutableArray<GroupRow> Rows,
        DateTime? SessionDeadline
    )
    {
        /// <summary>
        /// Ends every session that the read met past its deadline: those of the group's members and the session that
        /// joins or leaves.
        /// </summary>
        public void EndExpiredSessions()
        {
            var now = Change.Now;
            var expired = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var row in Rows)
            {
                if (row.Deadline is { } deadline && deadline <= now)
                {
                    expired.Add(row.Session);
                }
            }

            if (SessionDeadline is { } own && own <= now)
            {
                expired.Add(Session);
            }

            foreach (var session in expired)
            {
                Change.EndSession(session);
            }
        }
    }

    /// <summary>
    /// Holds one member row of a group with the deadline of its session.
    /// </summary>
    /// <param name="Session">The id of the session the member is bound to.</param>
    /// <param name="NodeId">The member's node id.</param>
    /// <param name="Deadline">The deadline of the session's row, or null when the row is gone.</param>
    private sealed record GroupRow(string Session, string NodeId, DateTime? Deadline)
    {
        /// <summary>Tells whether the member's session is live at an instant.</summary>
        /// <param name="now">The instant, on the server's clock.</param>
        /// <returns><see langword="true"/> when the session's row exists and its deadline is after the instant.</returns>
        public bool IsLiveAt(DateTime now) => Deadline is { } deadline && deadline > now;
    }

    /// <summary>
    /// Holds one row of a group's read: the keyspace's revision, the server's instant and, when the group has a
    /// member, the member with the deadline of its session.
    /// </summary>
    /// <param name="Revision">The keyspace's revision.</param>
    /// <param name="Now">The server's instant.</param>
    /// <param name="Session">The id of the member's session, or null when the group has no member.</param>
    /// <param name="NodeId">The member's node id.</param>
    /// <param name="Address">The member's address.</param>
    /// <param name="Names">The names of the member's metadata, ordinally sorted.</param>
    /// <param name="Values">The values of the member's metadata, in the order of the names.</param>
    /// <param name="Deadline">The deadline of the member's session.</param>
    private sealed record MemberRow(
        long Revision,
        DateTime Now,
        string? Session,
        string? NodeId,
        string? Address,
        string[]? Names,
        string[]? Values,
        DateTime? Deadline
    )
    {
        /// <summary>Tells whether the member's session has passed its deadline at the instant of the read.</summary>
        /// <returns><see langword="true"/> when the row holds a member whose session's deadline has passed.</returns>
        public bool IsExpired() => Deadline is { } deadline && deadline <= Now;
    }
}
