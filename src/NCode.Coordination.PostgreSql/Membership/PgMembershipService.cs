// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;
using Npgsql;
using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Membership;

/// <summary>
/// The membership groups of the PostgreSQL provider: each member is a row of the <c>members</c> table bound to its
/// session, so it is absent once its session ends. A join and a leave are transactions that lock the keyspace's row,
/// end the expired sessions they meet and log one entry that names the group. A read is one statement, unless it
/// meets an expired session: then it reads again in a transaction that first waits for the keyspace's writers. A
/// watch subscribes to the keyspace's change log and reads the group again after a resync and after each entry that
/// names the group or ends a session, yielding the view when its revision is above the last one yielded.
/// </summary>
/// <param name="table">The record table, whose keyspaces' ids and transactions the service shares.</param>
/// <param name="changeLog">The reader of a keyspace's change log.</param>
/// <param name="hub">The owner of the provider's listening connection and logs' fanouts.</param>
/// <exception cref="ArgumentNullException">An argument is null.</exception>
internal sealed class PgMembershipService(PgRecordTable table, PgChangeLog changeLog, PgLogHub hub)
    : IMembershipService
{
    /// <summary>
    /// The template of the statement that reads a group's rows beside the deadlines of their sessions.
    /// </summary>
    private const string SelectGroup = """
        SELECT m.session_id, m.node_id, s.deadline
        FROM {schema}.members m
        LEFT JOIN {schema}.sessions s ON s.keyspace_id = m.keyspace_id AND s.id = m.session_id
        WHERE m.keyspace_id = $1 AND m.group_idx = $2 AND m.group_depth = $3
        """;

    /// <summary>
    /// The template of the statement that reads the deadline of a session's row.
    /// </summary>
    private const string SelectSession = """
        SELECT deadline FROM {schema}.sessions WHERE keyspace_id = $1 AND id = $2
        """;

    /// <summary>
    /// The template of the statement that adds a member to a group, replacing its session's row.
    /// </summary>
    private const string UpsertMember = """
        INSERT INTO {schema}.members
            (keyspace_id, group_idx, group_depth, group_key, session_id, node_id, address, metadata_names, metadata_values)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
        ON CONFLICT (keyspace_id, group_idx, group_depth, session_id) DO UPDATE SET
            group_key = EXCLUDED.group_key, node_id = EXCLUDED.node_id, address = EXCLUDED.address,
            metadata_names = EXCLUDED.metadata_names, metadata_values = EXCLUDED.metadata_values
        """;

    /// <summary>
    /// The template of the statement that removes a session's member from a group.
    /// </summary>
    private const string DeleteMember = """
        DELETE FROM {schema}.members
        WHERE keyspace_id = $1 AND group_idx = $2 AND group_depth = $3 AND session_id = $4
        """;

    /// <summary>
    /// The template of the statement that reads a group's live members at the keyspace's revision.
    /// </summary>
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

    /// <summary>
    /// Finds a group's rows by its key.
    /// </summary>
    /// <param name="group">The group's key.</param>
    /// <returns>The group's key with the values that its rows are found by.</returns>
    internal static PgGroupAddress AddressOf(CoordinationKey group)
    {
        var index = PgKeys.Index(group);
        var depth = (short)group.PartitionDepth;
        var indexArgument = PgArgument.Typed(index, NpgsqlDbType.Bytea);

        return new PgGroupAddress(group, index, depth, indexArgument);
    }

    /// <summary>
    /// Builds what a join needs: the member's metadata is sorted ordinally by name into two arrays.
    /// </summary>
    /// <param name="address">The group's key and the values that its rows are found by.</param>
    /// <param name="member">The member that joins.</param>
    /// <returns>The request.</returns>
    private static PgJoinRequest JoinRequestOf(PgGroupAddress address, Member member)
    {
        var (names, values) = SortedMetadata(member.Metadata);
        var groupText = PgKeys.KeyText(address.Group);
        var namesArgument = PgArgument.Typed(names, NpgsqlDbType.Array | NpgsqlDbType.Text);
        var valuesArgument = PgArgument.Typed(values, NpgsqlDbType.Array | NpgsqlDbType.Text);

        return new PgJoinRequest(address, member, groupText, namesArgument, valuesArgument);
    }

    /// <summary>
    /// Copies a member's metadata into two arrays, sorted ordinally by name.
    /// </summary>
    /// <param name="metadata">The member's metadata.</param>
    /// <returns>The names and, at the same positions, the values.</returns>
    private static (string[] Names, string[] Values) SortedMetadata(
        ImmutableDictionary<string, string> metadata
    )
    {
        var pairs = new KeyValuePair<string, string>[metadata.Count];
        var next = 0;
        foreach (var pair in metadata)
        {
            pairs[next] = pair;
            next++;
        }

        Array.Sort(pairs, CompareNames);

        var names = new string[pairs.Length];
        var values = new string[pairs.Length];
        for (var index = 0; index < pairs.Length; index++)
        {
            names[index] = pairs[index].Key;
            values[index] = pairs[index].Value;
        }

        return (names, values);
    }

    /// <summary>
    /// Compares two metadata entries ordinally by name.
    /// </summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns>A value that is negative, zero or positive as the first name sorts before, with or after the second.</returns>
    private static int CompareNames(
        KeyValuePair<string, string> left,
        KeyValuePair<string, string> right
    )
    {
        return string.CompareOrdinal(left.Key, right.Key);
    }

    /// <summary>
    /// Tells whether a live member of the group, bound to another session, already holds the joining member's node id.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the join needs.</param>
    /// <returns><see langword="true"/> when the node id is taken.</returns>
    private static bool IsNodeIdTaken(PgMembershipStart start, PgJoinRequest request)
    {
        var now = start.Change.Now;
        foreach (var row in start.Rows)
        {
            if (HoldsNodeIdForOtherSession(row, now, request))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decides a join from the rows that were read.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the join needs.</param>
    /// <returns>The status of the join.</returns>
    private static MembershipJoinStatus DecideJoin(PgMembershipStart start, PgJoinRequest request)
    {
        var now = start.Change.Now;
        if (start.SessionDeadline is not { } deadline || deadline <= now)
        {
            return MembershipJoinStatus.SessionEnded;
        }

        if (IsNodeIdTaken(start, request))
        {
            return MembershipJoinStatus.NodeIdTaken;
        }

        return MembershipJoinStatus.Joined;
    }

    /// <summary>
    /// Tells whether a member row is live, has the joining member's node id and is bound to another session.
    /// </summary>
    /// <param name="row">The member row.</param>
    /// <param name="now">The server's instant.</param>
    /// <param name="request">What the join needs.</param>
    /// <returns><see langword="true"/> when the row keeps the node id from the joining member.</returns>
    private static bool HoldsNodeIdForOtherSession(
        PgGroupRow row,
        DateTime now,
        PgJoinRequest request
    )
    {
        var member = request.Member;
        var sameNode = string.Equals(row.NodeId, member.NodeId, StringComparison.Ordinal);
        var sameSession = string.Equals(
            row.Session,
            member.Session.Value,
            StringComparison.Ordinal
        );

        return row.IsLiveAt(now) && sameNode && !sameSession;
    }

    /// <summary>
    /// Tells whether the group has a live member bound to a session.
    /// </summary>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="session">The id of the session.</param>
    /// <returns><see langword="true"/> when the session's member is live.</returns>
    private static bool HasLiveMember(PgMembershipStart start, string session)
    {
        var now = start.Change.Now;
        foreach (var row in start.Rows)
        {
            if (IsLiveMemberOf(row, now, session))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether a read met a member whose session's deadline has passed.
    /// </summary>
    /// <param name="rows">The rows that were read.</param>
    /// <returns><see langword="true"/> when a row has expired.</returns>
    private static bool HoldsExpiredRow(ImmutableArray<PgMemberRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.IsExpired())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether a member row is live and is bound to a session.
    /// </summary>
    /// <param name="row">The member row.</param>
    /// <param name="now">The server's instant.</param>
    /// <param name="session">The id of the session.</param>
    /// <returns><see langword="true"/> when the row is the session's live member.</returns>
    private static bool IsLiveMemberOf(PgGroupRow row, DateTime now, string session)
    {
        var sameSession = string.Equals(row.Session, session, StringComparison.Ordinal);

        return row.IsLiveAt(now) && sameSession;
    }

    /// <summary>
    /// Tells whether an entry of the change log may change a group's members: it names the group or ends a session.
    /// </summary>
    /// <param name="entry">The entry, or null.</param>
    /// <param name="address">The group's key and the values that its rows are found by.</param>
    /// <returns><see langword="true"/> when the group's view may have changed.</returns>
    internal static bool Concerns(PgChange? entry, PgGroupAddress address)
    {
        if (entry is null)
        {
            return false;
        }

        if (entry.EndedSession is not null)
        {
            return true;
        }

        return entry.GroupDepth == address.Depth
            && entry.GroupIndex is { } groupIndex
            && groupIndex.AsSpan().SequenceEqual(address.Index);
    }

    /// <summary>
    /// Reads a row of the result of the statement that reads a group's rows beside the deadlines of their sessions.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The row.</returns>
    private static PgGroupRow ReadGroupRow(NpgsqlDataReader reader)
    {
        var session = reader.GetString(0);
        var nodeId = reader.GetString(1);
        var deadline = PgColumns.ReadNullableUtc(reader, 2);

        return new PgGroupRow(session, nodeId, deadline);
    }

    /// <summary>
    /// Reads a row of the result of the statement that lists a group's members.
    /// </summary>
    /// <param name="reader">The reader, on a row.</param>
    /// <returns>The row.</returns>
    private static PgMemberRow ReadMemberRow(NpgsqlDataReader reader)
    {
        var revision = reader.GetInt64(0);
        var now = PgColumns.ReadUtc(reader, 1);
        var session = PgColumns.ReadNullableText(reader, 2);
        var nodeId = PgColumns.ReadNullableText(reader, 3);
        var address = PgColumns.ReadNullableText(reader, 4);
        var names = PgColumns.ReadNullableTexts(reader, 5);
        var values = PgColumns.ReadNullableTexts(reader, 6);
        var deadline = PgColumns.ReadNullableUtc(reader, 7);

        return new PgMemberRow(revision, now, session, nodeId, address, names, values, deadline);
    }

    /// <summary>
    /// Reads the results of the first batch of a join or a leave.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What the batch's four statements returned.</returns>
    private static async ValueTask<(
        ImmutableArray<long> Revisions,
        ImmutableArray<DateTime> Clocks,
        ImmutableArray<PgGroupRow> Rows,
        ImmutableArray<DateTime> Deadlines
    )> ReadStartAsync(PgResults results, CancellationToken cancellationToken)
    {
        var revisions = await results
            .ReadAsync(reader => reader.GetInt64(0), cancellationToken)
            .ConfigureAwait(false);
        var clocks = await results
            .ReadAsync(reader => PgColumns.ReadUtc(reader, 0), cancellationToken)
            .ConfigureAwait(false);
        var rows = await results.ReadAsync(ReadGroupRow, cancellationToken).ConfigureAwait(false);
        var deadlines = await results
            .ReadAsync(reader => PgColumns.ReadUtc(reader, 0), cancellationToken)
            .ConfigureAwait(false);

        return (revisions, clocks, rows, deadlines);
    }

    /// <summary>
    /// Reads the result of the statement that lists a group's members.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The rows; none when the keyspace's row does not exist.</returns>
    private static async ValueTask<ImmutableArray<PgMemberRow>> ReadMembersAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return await results.ReadAsync(ReadMemberRow, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the view of a group from the rows that were read, keeping the live members only.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="rows">The rows, which are not empty.</param>
    /// <returns>The view at the keyspace's revision.</returns>
    private static MembershipView ToView(CoordinationKey group, ImmutableArray<PgMemberRow> rows)
    {
        var members = new List<Member>(rows.Length);
        foreach (var row in rows)
        {
            var member = ToLiveMember(group.Keyspace, row);
            if (member is not null)
            {
                members.Add(member);
            }
        }

        var revision = new Revision(rows[0].Revision);

        return new MembershipView(group, revision, members);
    }

    /// <summary>
    /// Builds the member that a row holds when its session's deadline is after the instant of the read.
    /// </summary>
    /// <param name="keyspace">The group's keyspace.</param>
    /// <param name="row">The row.</param>
    /// <returns>The member, or null when the row holds no member or its session has expired.</returns>
    private static Member? ToLiveMember(Keyspace keyspace, PgMemberRow row)
    {
        if (
            row
                is not {
                    Session: { } session,
                    NodeId: { } nodeId,
                    Address: { } address,
                    Names: { } names,
                    Values: { } values,
                    Deadline: not null,
                }
            || row.IsExpired()
        )
        {
            return null;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        for (var index = 0; index < names.Length; index++)
        {
            builder.Add(names[index], values[index]);
        }

        var metadata = builder.ToImmutable();
        var sessionId = new SessionId(keyspace, session);

        return new Member(sessionId, nodeId, address) { Metadata = metadata };
    }

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

    /// <summary>
    /// Gets the statement that reads a group's rows beside the deadlines of their sessions.
    /// </summary>
    private string SelectGroupSql { get; } = table.Apply(SelectGroup);

    /// <summary>
    /// Gets the statement that reads the deadline of a session's row.
    /// </summary>
    private string SelectSessionSql { get; } = table.Apply(SelectSession);

    /// <summary>
    /// Gets the statement that adds a member to a group.
    /// </summary>
    private string UpsertMemberSql { get; } = table.Apply(UpsertMember);

    /// <summary>
    /// Gets the statement that removes a session's member from a group.
    /// </summary>
    private string DeleteMemberSql { get; } = table.Apply(DeleteMember);

    /// <summary>
    /// Gets the statement that reads a group's live members.
    /// </summary>
    private string SelectMembersSql { get; } = table.Apply(SelectMembers);

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

        var address = AddressOf(group);
        var request = JoinRequestOf(address, member);

        return await Table
            .RunInKeyspaceAsync<MembershipJoinStatus>(
                group.Keyspace,
                (id, token) => JoinInKeyspaceAsync(id, request, token),
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

        var address = AddressOf(group);
        var request = new PgLeaveRequest(address, session);

        await Table
            .RunInKeyspaceAsync<bool>(
                group.Keyspace,
                (id, token) => LeaveInKeyspaceAsync(id, request, token),
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

        var address = AddressOf(group);

        return await ReadViewAsync(address, cancellationToken).ConfigureAwait(false);
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
        var (logId, subscriber) = await SubscribeToChangesAsync(group.Keyspace, cancellationToken)
            .ConfigureAwait(false);
        var address = AddressOf(group);

        try
        {
            var last = await ReadViewAsync(address, cancellationToken).ConfigureAwait(false);
            yield return last;

            await foreach (
                var item in subscriber.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                if (!item.IsResync && !Concerns(item.Entry, address))
                {
                    continue;
                }

                var view = await ReadViewAsync(address, cancellationToken).ConfigureAwait(false);
                if (view.Revision.Value > last.Revision.Value)
                {
                    last = view;
                    yield return view;
                }
            }
        }
        finally
        {
            await Hub.UnsubscribeAsync(logId, subscriber, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves a keyspace and subscribes to its change log.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The log's id and the subscriber that reads it.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<(
        PgLogId LogId,
        PgLogSubscriber<PgChange> Subscriber
    )> SubscribeToChangesAsync(Keyspace keyspace, CancellationToken cancellationToken)
    {
        var keyspaceId = await Table
            .ResolveAsync(keyspace, cancellationToken)
            .ConfigureAwait(false);

        var logId = new PgLogId(keyspaceId, null);
        var subscriber = await Hub.SubscribeAsync(
                logId,
                () => CreateFanout(keyspaceId),
                null,
                cancellationToken
            )
            .ConfigureAwait(false);

        return (logId, subscriber);
    }

    /// <summary>
    /// Creates the fanout that reads a keyspace's change log.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <returns>A fanout that reads the keyspace's change log.</returns>
    private PgLogFanout<PgChange> CreateFanout(long keyspaceId)
    {
        return new PgLogFanout<PgChange>(
            (cursor, limit, readToken) => ChangeLog.ReadAsync(keyspaceId, cursor, limit, readToken)
        );
    }

    /// <summary>
    /// Reads a group's live members at the keyspace's revision. A member whose session's deadline has passed makes the
    /// read run again after it waits for the keyspace's writers, so the view follows the write that ends the session.
    /// </summary>
    /// <param name="address">The group's key and the values that its rows are found by.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The view.</returns>
    /// <exception cref="CoordinationException">The server could not be reached or failed.</exception>
    private async ValueTask<MembershipView> ReadViewAsync(
        PgGroupAddress address,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .RunInKeyspaceAsync<MembershipView>(
                address.Group.Keyspace,
                (id, token) => ReadViewInKeyspaceAsync(id, address, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a group's view in a keyspace: one statement, then, when it met a member whose session has expired, a
    /// second read that follows the keyspace's writers.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="address">The group's key and the values that its rows are found by.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The view.</returns>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    private async ValueTask<MembershipView> ReadViewInKeyspaceAsync(
        long id,
        PgGroupAddress address,
        CancellationToken cancellationToken
    )
    {
        var statement = SelectMembersStatement(id, address);

        var first = await Table
            .ExecuteAsync([statement], ReadMembersAsync, cancellationToken)
            .ConfigureAwait(false);

        PgKeyspaceMissingException.ThrowIfNoRow(first.Length);

        if (!HoldsExpiredRow(first))
        {
            return ToView(address.Group, first);
        }

        var second = await Table
            .ReadAfterWritersAsync(id, statement, ReadMembersAsync, cancellationToken)
            .ConfigureAwait(false);

        PgKeyspaceMissingException.ThrowIfNoRow(second.Length);

        return ToView(address.Group, second);
    }

    /// <summary>
    /// Builds the statement that reads a group's members.
    /// </summary>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="address">The group's key and the values that its rows are found by.</param>
    /// <returns>The statement.</returns>
    private PgStatement SelectMembersStatement(long keyspaceId, PgGroupAddress address)
    {
        return new PgStatement(SelectMembersSql, keyspaceId, address.IndexArgument, address.Depth);
    }

    /// <summary>
    /// Joins a group in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the join needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The status of the join.</returns>
    private async ValueTask<MembershipJoinStatus> JoinInKeyspaceAsync(
        long id,
        PgJoinRequest request,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => JoinInTransactionAsync(transaction, id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, decides the join from the group's rows and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the join needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The status of the join.</returns>
    private async ValueTask<MembershipJoinStatus> JoinInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgJoinRequest request,
        CancellationToken cancellationToken
    )
    {
        var session = request.Member.Session.Value;
        var start = await BeginAsync(transaction, id, request.Address, session, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions();

        var status = DecideJoin(start, request);
        if (status == MembershipJoinStatus.Joined)
        {
            WriteMember(id, start, request);
        }

        await Table
            .CommitAsync(transaction, start.Change, id, cancellationToken)
            .ConfigureAwait(false);
        return status;
    }

    /// <summary>
    /// Writes the member's row in the change that names the group.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the join needs.</param>
    private void WriteMember(long id, PgMembershipStart start, PgJoinRequest request)
    {
        var address = request.Address;
        var member = request.Member;
        var upsert = new PgStatement(
            UpsertMemberSql,
            id,
            address.IndexArgument,
            address.Depth,
            request.GroupText,
            member.Session.Value,
            member.NodeId,
            member.Address,
            request.Names,
            request.Values
        );

        _ = start.Change.Begin();
        start.Change.NameGroup(address.Index, address.Depth);
        start.Change.Write(upsert);
    }

    /// <summary>
    /// Leaves a group in a keyspace, in a transaction.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the leave needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>Always <see langword="true"/>: a session that holds no member leaves nothing.</returns>
    private async ValueTask<bool> LeaveInKeyspaceAsync(
        long id,
        PgLeaveRequest request,
        CancellationToken cancellationToken
    )
    {
        return await Table
            .TransactAsync(
                PgIsolation.ReadCommitted,
                (transaction, token) => LeaveInTransactionAsync(transaction, id, request, token),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a change, removes the session's member when the group has it live and commits the change.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="request">What the leave needs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    private async ValueTask<bool> LeaveInTransactionAsync(
        PgTransaction transaction,
        long id,
        PgLeaveRequest request,
        CancellationToken cancellationToken
    )
    {
        var session = request.Session.Value;
        var start = await BeginAsync(transaction, id, request.Address, session, cancellationToken)
            .ConfigureAwait(false);

        start.EndExpiredSessions();

        if (HasLiveMember(start, session))
        {
            WriteLeave(id, start, request);
        }

        await Table
            .CommitAsync(transaction, start.Change, id, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Writes the removal of the session's member in the change that names the group.
    /// </summary>
    /// <param name="id">The keyspace's id.</param>
    /// <param name="start">What the transaction's first batch read.</param>
    /// <param name="request">What the leave needs.</param>
    private void WriteLeave(long id, PgMembershipStart start, PgLeaveRequest request)
    {
        var address = request.Address;
        var delete = new PgStatement(
            DeleteMemberSql,
            id,
            address.IndexArgument,
            address.Depth,
            request.Session.Value
        );

        _ = start.Change.Begin();
        start.Change.NameGroup(address.Index, address.Depth);
        start.Change.Write(delete);
    }

    /// <summary>
    /// Reads what the first batch of a join or a leave needs: it locks the keyspace's row, reads the server's instant,
    /// the group's rows with the deadlines of their sessions, and the deadline of the asked session's row.
    /// </summary>
    /// <param name="transaction">The transaction, which has not run a batch.</param>
    /// <param name="keyspaceId">The keyspace's id.</param>
    /// <param name="address">The group's key and the values that its rows are found by.</param>
    /// <param name="session">The id of the session that joins or leaves.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What the batch read.</returns>
    /// <exception cref="PgKeyspaceMissingException">The keyspace's row does not exist.</exception>
    private async ValueTask<PgMembershipStart> BeginAsync(
        PgTransaction transaction,
        long keyspaceId,
        PgGroupAddress address,
        string session,
        CancellationToken cancellationToken
    )
    {
        var lockKeyspace = Table.LockKeyspace(keyspaceId);
        var clock = Table.Clock();
        var selectGroup = new PgStatement(
            SelectGroupSql,
            keyspaceId,
            address.IndexArgument,
            address.Depth
        );
        var selectSession = new PgStatement(SelectSessionSql, keyspaceId, session);
        PgStatement[] statements = [lockKeyspace, clock, selectGroup, selectSession];

        var read = await transaction
            .FirstAsync(statements, ReadStartAsync, cancellationToken)
            .ConfigureAwait(false);

        PgKeyspaceMissingException.ThrowIfNoRow(read.Revisions.Length);

        var now = read.Clocks[0];
        var origin = new PgChangeOrigin(keyspaceId, read.Revisions[0], now);
        var change = Table.NewChange(origin);
        DateTime? sessionDeadline = read.Deadlines.Length == 0 ? null : read.Deadlines[0];

        return new PgMembershipStart(change, session, read.Rows, sessionDeadline);
    }
}
