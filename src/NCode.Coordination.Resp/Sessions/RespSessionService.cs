// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Sessions;

/// <summary>
/// The sessions of the RESP provider: each session is a field of the keyspace's <c>sessions</c> hash with its time to
/// live and a deadline in its <c>session-deadlines</c> sorted set, and ending one removes what is bound to it. One
/// keyspace script serves each operation, and a wait for a session's end polls until the session has ended or the
/// caller cancels.
/// </summary>
/// <param name="table">The table that runs the keyspace scripts.</param>
/// <param name="tokens">The source of session ids.</param>
/// <exception cref="ArgumentNullException">The table or the tokens are null.</exception>
internal class RespSessionService(RespRecordTable table, IRespTokens tokens) : ISessionService
{
    /// <summary>
    /// The text a session id starts with.
    /// </summary>
    private const string Prefix = "s-";

    /// <summary>
    /// Gets the table that runs the keyspace scripts.
    /// </summary>
    private RespRecordTable Table { get; } =
        table ?? throw new ArgumentNullException(nameof(table));

    /// <summary>
    /// Gets the source of session ids.
    /// </summary>
    private IRespTokens Tokens { get; } = tokens ?? throw new ArgumentNullException(nameof(tokens));

    /// <inheritdoc />
    public async ValueTask<SessionGrant> OpenAsync(
        Keyspace keyspace,
        TimeToLive timeToLive,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(keyspace);
        ArgumentNullException.ThrowIfNull(timeToLive);
        RespCall.Check(options, cancellationToken);

        var token = Tokens.Next(Prefix);
        var milliseconds = RespCall.ToMilliseconds(timeToLive);

        RedisValue[] arguments = [token, milliseconds];
        var reply = await Table
            .EvaluateAsync(RespScript.SessionOpen, keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length == 0 => new SessionGrant(
                new SessionId(keyspace, token),
                new TimeToLive(TimeSpan.FromMilliseconds(milliseconds))
            ),
            _ => throw RespReplies.Unexpected(reply),
        };
    }

    /// <inheritdoc />
    public async ValueTask<KeepAliveResult> KeepAliveAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        RespCall.Check(options, cancellationToken);

        RedisValue[] arguments = [session.Value];
        var reply = await Table
            .EvaluateAsync(
                RespScript.SessionKeepAlive,
                session.Keyspace,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length == 1 => KeepAliveResult.Renewed(
                new TimeToLive(TimeSpan.FromMilliseconds(RespReplies.Integer(fields[0])))
            ),
            1 when fields.Length == 0 => KeepAliveResult.Ended,
            _ => throw RespReplies.Unexpected(reply),
        };
    }

    /// <inheritdoc />
    public async ValueTask CloseAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        RespCall.Check(options, cancellationToken);

        RedisValue[] arguments = [session.Value];
        var reply = await Table
            .EvaluateAsync(RespScript.SessionClose, session.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        if (status != 0)
        {
            throw RespReplies.Unexpected(reply);
        }
    }

    /// <inheritdoc />
    public async ValueTask WaitForEndAsync(
        SessionId session,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        RespCall.Check(options, cancellationToken);

        await RespPolling
            .UntilAsync(token => HasEndedAsync(session, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the server whether a session has ended.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the server.</param>
    /// <returns><see langword="true"/> when the session is no longer live.</returns>
    /// <exception cref="CoordinationException">The call failed or the reply was not one the script sends.</exception>
    private async ValueTask<bool> HasEndedAsync(
        SessionId session,
        CancellationToken cancellationToken
    )
    {
        RedisValue[] arguments = [session.Value];
        var reply = await Table
            .EvaluateAsync(RespScript.SessionCheck, session.Keyspace, arguments, cancellationToken)
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status switch
        {
            0 => false,
            1 => true,
            _ => throw RespReplies.Unexpected(reply),
        };
    }
}
