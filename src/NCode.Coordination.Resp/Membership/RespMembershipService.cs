// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Membership;

/// <summary>
/// The membership groups of the RESP provider: each operation is one membership script that runs over the keyspace's membership table.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespMembershipService(RespServer server) : IMembershipService
{
    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Reads the members from the script reply.
    /// </summary>
    /// <param name="reply">The script's reply.</param>
    /// <returns>An array of fields (count, then quadruplets of nodeId, sessionId, address, metadata).</returns>
    private static RedisResult[] ReadMembers(RedisResult reply)
    {
        var (status, fields) = RespReplies.Script(reply);
        if (status != 0 || fields.Length < 1)
        {
            throw RespReplies.Unexpected(reply);
        }

        return fields;
    }

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

        var keyspace = group.Keyspace;
        var groupId = RespKeys.Member(group);
        var sessionId = member.Session.Value;
        var nodeId = member.NodeId;
        var address = member.Address;
        var metadata = string.Join('|', member.Metadata.Select(x => $"{x.Key}={x.Value}"));

        var keys = new[] { RespKeys.ForKeyspace(keyspace, "members") };
        var arguments = new RedisValue[] { "join", groupId, nodeId, sessionId, address, metadata };

        var reply = await Server.EvaluateAsync(
            RespScript.Membership,
            keys,
            arguments,
            cancellationToken
        );

        var (status, _) = RespReplies.Script(reply);
        return status switch
        {
            0 => MembershipJoinStatus.Joined,
            1 => MembershipJoinStatus.NodeIdTaken,
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

        var keyspace = group.Keyspace;
        var groupId = RespKeys.Member(group);
        var sessionId = session.Value;

        var keys = new[] { RespKeys.ForKeyspace(keyspace, "members") };
        var arguments = new RedisValue[] { "leave", groupId, "", sessionId, "", "" };

        var reply = await Server.EvaluateAsync(
            RespScript.Membership,
            keys,
            arguments,
            cancellationToken
        );

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

        var keyspace = group.Keyspace;
        var groupId = RespKeys.Member(group);

        var keys = new[] { RespKeys.ForKeyspace(keyspace, "members") };
        var arguments = new RedisValue[] { "list", groupId, "", "", "", "" };

        var reply = await Server.EvaluateAsync(
            RespScript.Membership,
            keys,
            arguments,
            cancellationToken
        );

        var fields = ReadMembers(reply);

        // First field is member count, then quadruplets of (nodeId, sessionId, address, metadata)
        if (fields.Length < 1)
        {
            throw RespReplies.Unexpected(reply);
        }

        var count = (long?)fields[0] ?? 0;
        if (fields.Length != 1 + count * 4)
        {
            throw RespReplies.Unexpected(reply);
        }

        var members = new List<Member>();
        for (int i = 0; i < count; i++)
        {
            var nodeId = (string?)fields[1 + i * 4];
            var sessionIdStr = (string?)fields[1 + i * 4 + 1];
            var address = (string?)fields[1 + i * 4 + 2];
            var metadataStr = (string?)fields[1 + i * 4 + 3];

            if (nodeId is null || sessionIdStr is null || address is null)
            {
                throw RespReplies.Unexpected(reply);
            }

            // Parse session ID
            var sessionId = new SessionId(keyspace, sessionIdStr);

            // Parse metadata
            var metadataDict = ImmutableDictionary<string, string>.Empty;
            if (!string.IsNullOrEmpty(metadataStr))
            {
                var metadataBuilder = metadataDict.ToBuilder();
                foreach (var pair in metadataStr.Split('|'))
                {
                    if (string.IsNullOrEmpty(pair))
                        continue;
                    var eqPos = pair.IndexOf('=');
                    if (eqPos > 0)
                    {
                        var key = pair.Substring(0, eqPos);
                        var value = pair.Substring(eqPos + 1);
                        metadataBuilder[key] = value;
                    }
                }
                metadataDict = metadataBuilder.ToImmutable();
            }

            var member = new Member(sessionId, nodeId, address) { Metadata = metadataDict };
            members.Add(member);
        }

        // Revision is 1 for now (membership doesn't track revisions yet in this simple implementation)
        var revision = new Revision(1);
        return new MembershipView(group, revision, members);
    }

    /// <inheritdoc />
#pragma warning disable CS8425
    public async IAsyncEnumerable<MembershipView> WatchAsync(
        CoordinationKey group,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
#pragma warning restore CS8425
    {
        ArgumentNullException.ThrowIfNull(group);

        // For now, yield the current view once, then end.
        // Full watch support with pub/sub channels will come later.
        var view = await ReadAsync(group, options, cancellationToken);
        yield return view;
    }
}
