// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;

namespace NCode.Coordination.Resp.Sets;

/// <summary>
/// The sets of the RESP provider: each is one server sorted set whose members are scored with their deadlines on the
/// server's clock, and every operation but clearing runs as one Lua script.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespSetService(RespServer server) : ISetService
{
    /// <summary>
    /// The kind of server key that holds a set.
    /// </summary>
    private const string Kind = "set";

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <inheritdoc />
    public async ValueTask<bool> AddAsync(
        CoordinationKey key,
        KeySegment member,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(timeToLive);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        var encoded = RespKeys.EncodeSegment(member);
        var milliseconds = RespCall.ToMilliseconds(timeToLive);
        var reply = await Server
            .EvaluateAsync(
                RespScript.SetAdd,
                [serverKey],
                [encoded, milliseconds, CoordinationLimits.MaxSetMembers],
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        switch (status)
        {
            case 1:
                throw new CoordinationException(
                    CoordinationErrorCodes.LimitExceeded,
                    outcomeUnknown: false,
                    $"A set holds at most {CoordinationLimits.MaxSetMembers} members."
                );

            case 0 when fields.Length == 1:
                return RespReplies.Integer(fields[0]) == 1;

            default:
                throw RespReplies.Unexpected(reply);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> RemoveAsync(
        CoordinationKey key,
        KeySegment member,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        var encoded = RespKeys.EncodeSegment(member);
        var reply = await Server
            .EvaluateAsync(RespScript.SetRemove, [serverKey], [encoded], cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        if (status == 0 && fields.Length == 1)
        {
            return RespReplies.Integer(fields[0]) == 1;
        }

        throw RespReplies.Unexpected(reply);
    }

    /// <inheritdoc />
    public async ValueTask<ImmutableArray<KeySegment>> ListAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        var reply = await Server
            .EvaluateAsync(RespScript.SetList, [serverKey], [], cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);
        if (status != 0)
        {
            throw RespReplies.Unexpected(reply);
        }

        var members = ImmutableArray.CreateBuilder<KeySegment>(fields.Length);
        foreach (var field in fields)
        {
            var text = (string?)field;
            if (text is null)
            {
                throw RespReplies.Unexpected(reply);
            }

            KeySegment member;
            try
            {
                member = RespKeys.DecodeSegment(text);
            }
            catch (FormatException)
            {
                throw RespReplies.Unexpected(reply);
            }

            members.Add(member);
        }

        return members.MoveToImmutable();
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        RespCall.Check(options, cancellationToken);

        var serverKey = RespKeys.ForKey(key, Kind);
        await Server
            .ExecuteAsync(RespCommands.Delete, [serverKey], cancellationToken)
            .ConfigureAwait(false);
    }
}
