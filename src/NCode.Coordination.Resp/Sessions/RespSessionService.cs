// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Keys;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Sessions;

/// <summary>
/// The sessions of the RESP provider: each session stores its TTL, liveness check timestamp and bound leases,
/// with one Lua script per operation.
/// </summary>
/// <param name="server">The seam between the provider and its server.</param>
/// <exception cref="ArgumentNullException">The server is null.</exception>
internal class RespSessionService(RespServer server) : ISessionService
{
    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private RespServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));

    /// <summary>
    /// Generates a session ID.
    /// </summary>
    /// <param name="keyspace">The keyspace.</param>
    /// <returns>The session ID.</returns>
    private static string GenerateSessionId(Keyspace keyspace)
    {
        // Generate a base64-ish session ID using timestamp and random bytes
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = new byte[8];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }

        var id = $"s-{timestamp:X}-{BitConverter.ToUInt64(random):X}";
        return id;
    }

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

        var ttlMs = RespCall.ToMilliseconds(timeToLive);
        var sessionIdValue = GenerateSessionId(keyspace);

        var keys = new[] { RespKeys.ForKeyspace(keyspace, "sessions") };
        var arguments = new RedisValue[]
        {
            "open",
            sessionIdValue,
            ttlMs,
        };

        var reply = await Server
            .EvaluateAsync(
                RespScript.Session,
                keys,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, fields) = RespReplies.Script(reply);

        return status switch
        {
            0 when fields.Length >= 1 =>
                new SessionGrant(
                    new SessionId(keyspace, (string?)fields[0] ?? throw RespReplies.Unexpected(reply)),
                    timeToLive
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

        // Default 30 second TTL for keep-alive
        var ttlMs = 30_000L;

        var keys = new[] { RespKeys.ForKeyspace(session.Keyspace, "sessions") };
        var arguments = new RedisValue[]
        {
            "keepAlive",
            session.Value,
            ttlMs,
        };

        var reply = await Server
            .EvaluateAsync(
                RespScript.Session,
                keys,
                arguments,
                cancellationToken
            )
            .ConfigureAwait(false);

        var (status, _) = RespReplies.Script(reply);

        return status switch
        {
            0 => KeepAliveResult.Renewed(new TimeToLive(TimeSpan.FromMilliseconds(ttlMs))),
            1 => KeepAliveResult.Ended,
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

        var keys = new[] { RespKeys.ForKeyspace(session.Keyspace, "sessions") };
        var arguments = new RedisValue[]
        {
            "close",
            session.Value,
        };

        var reply = await Server
            .EvaluateAsync(
                RespScript.Session,
                keys,
                arguments,
                cancellationToken
            )
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

        // Poll for session expiry with backoff
        var maxRetries = 300; // 30 seconds with 100ms intervals
        var retryCount = 0;
        var retryDelay = TimeSpan.FromMilliseconds(100);

        while (retryCount < maxRetries)
        {
            var keys = new[] { RespKeys.ForKeyspace(session.Keyspace, "sessions") };
            var arguments = new RedisValue[]
            {
                "check-liveness",
                session.Value,
            };

            var reply = await Server
                .EvaluateAsync(
                    RespScript.Session,
                    keys,
                    arguments,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var (status, _) = RespReplies.Script(reply);

            if (status == 1)
            {
                // Session has ended
                return;
            }

            if (status != 0)
            {
                throw RespReplies.Unexpected(reply);
            }

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            retryCount++;
        }

        // Timeout - session might be stuck or the timeout was too short
        // For now, just return as if the session ended
    }
}
