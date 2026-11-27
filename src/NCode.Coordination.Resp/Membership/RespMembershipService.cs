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
/// each operation, and a watch subscribes to the keyspace's watch channel and lists the group again when an event
/// names the group or the channel resyncs.
/// </summary>
/// <param name="table">The table that runs the keyspace scripts.</param>
/// <param name="subscriptions">Opens the provider's subscriptions to numbered channels.</param>
/// <exception cref="ArgumentNullException">The table or the subscriptions are null.</exception>
internal class RespMembershipService(RespRecordTable table, RespSubscriptions subscriptions)
    : IMembershipService
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

    /// <summary>
    /// Gets the object that opens the provider's subscriptions to numbered channels.
    /// </summary>
    private RespSubscriptions Subscriptions { get; } =
        subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

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
    public IAsyncEnumerable<MembershipView> WatchAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(group);
        RespCall.Check(options, cancellationToken);

        return WatchCoreAsync(group, cancellationToken);
    }

    /// <summary>
    /// Reads the revision of a keyspace, which is the number of its watch channel.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The revision's value.</returns>
    private async ValueTask<long> ReadNumberAsync(
        Keyspace keyspace,
        CancellationToken cancellationToken
    )
    {
        var revision = await Table
            .ReadRevisionAsync(keyspace, cancellationToken)
            .ConfigureAwait(false);

        return revision.Value;
    }

    /// <summary>
    /// Lists a group, reporting a failure instead of throwing it so that a stream can survive it.
    /// </summary>
    /// <param name="group">The group to list.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The view of the group, or null when the read failed.</returns>
    private async ValueTask<MembershipView?> TryReadAsync(
        CoordinationKey group,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await ReadAsync(group, null, cancellationToken).ConfigureAwait(false);
        }
        catch (CoordinationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Streams the views of a watch: the group at the start, then again when an event names the group or the channel
    /// resyncs, each only when its revision is greater than the last one yielded.
    /// </summary>
    /// <param name="group">The group to watch.</param>
    /// <param name="cancellationToken">The token that cancels the stream.</param>
    /// <returns>A stream of views.</returns>
    private async IAsyncEnumerable<MembershipView> WatchCoreAsync(
        CoordinationKey group,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var channel = RespRecordTable.WatchChannel(group.Keyspace);
        var reader = await Subscriptions
            .OpenAsync(channel, token => ReadNumberAsync(group.Keyspace, token), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var name = RespKeys.Member(group);
            var changed = new RespWatchLine('m', name);
            MembershipView? last = null;
            await foreach (var item in reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!item.IsResync)
                {
                    if (!RespWatchLine.TryParse(item.Message, out var lines))
                    {
                        reader.MarkInDoubt();
                        continue;
                    }

                    if (!lines.Contains(changed))
                    {
                        continue;
                    }
                }

                MembershipView? view;
                if (last is null)
                {
                    view = await ReadAsync(group, null, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    view = await TryReadAsync(group, cancellationToken).ConfigureAwait(false);
                    if (view is null)
                    {
                        reader.MarkInDoubt();
                        continue;
                    }
                }

                if (last is null || view.Revision > last.Revision)
                {
                    last = view;
                    yield return view;
                }
            }
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }
}
