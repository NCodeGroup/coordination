// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Membership;

/// <summary>
/// The membership groups of the RESP provider: each member is an entry in the keyspace's <c>members</c> and
/// <c>member-fields</c> keys bound to its session, so it is absent once its session ends. One keyspace script serves
/// each operation, and a watch polls the keyspace's revision until the caller cancels, and lists the group again only
/// once the revision has moved.
/// </summary>
/// <param name="table">The table that runs the keyspace scripts.</param>
/// <exception cref="ArgumentNullException">The table is null.</exception>
internal class RespMembershipService(RespRecordTable table) : IMembershipService
{
    /// <summary>
    /// The number of fields a listed member takes in the reply: node id, session, address and metadata.
    /// </summary>
    private const int MemberFields = 4;

    /// <summary>
    /// Gets the table that runs the keyspace scripts.
    /// </summary>
    private RespRecordTable Table { get; } =
        table ?? throw new ArgumentNullException(nameof(table));

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
        RespCall.Check(options, cancellationToken);

        var name = RespKeys.Member(group);
        var metadata = RespMembershipMetadata.Encode(member.Metadata);
        RedisValue[] arguments =
        [
            name,
            member.NodeId,
            member.Session.Value,
            member.Address,
            metadata,
        ];
        var reply = await Table
            .EvaluateAsync(RespScript.MemberJoin, group.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status switch
        {
            0 => MembershipJoinStatus.Joined,
            1 => MembershipJoinStatus.NodeIdTaken,
            2 => MembershipJoinStatus.SessionEnded,
            _ => throw RespReplies.Unexpected(reply),
        };
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
        RespCall.Check(options, cancellationToken);

        var name = RespKeys.Member(group);
        RedisValue[] arguments = [name, session.Value];
        var reply = await Table
            .EvaluateAsync(RespScript.MemberLeave, group.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);
        if (status != 0)
        {
            throw RespReplies.Unexpected(reply);
        }
    }

    /// <inheritdoc />
    public async ValueTask<MembershipView> ReadAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        RespCall.Check(options, cancellationToken);

        var name = RespKeys.Member(group);
        RedisValue[] arguments = [name];
        var reply = await Table
            .EvaluateAsync(RespScript.MemberList, group.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        if (status != 0 || fields.Length < 1 || (fields.Length - 1) % MemberFields != 0)
        {
            throw RespReplies.Unexpected(reply);
        }

        var revision = RespRecordTable.ReadRevision(fields[0], reply);
        var members = new List<Member>((fields.Length - 1) / MemberFields);
        for (var index = 1; index < fields.Length; index += MemberFields)
        {
            var node = (string?)fields[index];
            var session = (string?)fields[index + 1];
            var address = (string?)fields[index + 2];
            var encoded = (string?)fields[index + 3];
            if (
                node is null
                || session is null
                || address is null
                || encoded is null
                || !RespMembershipMetadata.TryDecode(encoded, out var metadata)
            )
            {
                throw RespReplies.Unexpected(reply);
            }

            var id = new SessionId(group.Keyspace, session);
            var member = new Member(id, node, address) { Metadata = metadata };
            members.Add(member);
        }

        return new MembershipView(group, revision, members);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<MembershipView> WatchAsync(
        CoordinationKey group,
        OperationOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        RespCall.Check(options, cancellationToken);

        var last = await ReadAsync(group, options, cancellationToken).ConfigureAwait(false);
        yield return last;

        while (true)
        {
            await Task.Delay(RespPolling.Interval, cancellationToken).ConfigureAwait(false);

            var revision = await ReadRevisionAsync(group.Keyspace, cancellationToken)
                .ConfigureAwait(false);
            if (revision.Value <= last.Revision.Value)
            {
                continue;
            }

            var view = await ReadAsync(group, options, cancellationToken).ConfigureAwait(false);
            if (view.Revision.Value > last.Revision.Value)
            {
                last = view;
                yield return view;
            }
        }
    }

    /// <summary>
    /// Reads the revision of a keyspace, which a membership change takes.
    /// </summary>
    /// <param name="keyspace">The keyspace to read.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The keyspace's current revision.</returns>
    /// <exception cref="CoordinationException">The server's reply is not a revision.</exception>
    private async ValueTask<Revision> ReadRevisionAsync(
        Keyspace keyspace,
        CancellationToken cancellationToken
    )
    {
        var reply = await Table
            .EvaluateAsync(RespScript.KeyspaceRevision, keyspace, [], cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        if (status != 0 || fields.Length != 1)
        {
            throw RespReplies.Unexpected(reply);
        }

        return RespRecordTable.ReadRevision(fields[0], reply);
    }
}
