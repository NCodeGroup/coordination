// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Translates the failures of the StackExchange.Redis client into coordination failures that say whether the call may
/// have taken effect.
/// </summary>
internal static class RespFailures
{
    private const string ErrorPrefix = "ERR ";

    /// <summary>
    /// Returns whether an exception is a failure of the client or the server, which <see cref="Translate"/> handles.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True for a <see cref="RedisException"/>, a <see cref="RedisTimeoutException"/> or a <see cref="RedisCommandException"/>.</returns>
    /// <exception cref="ArgumentNullException">The exception is null.</exception>
    public static bool IsClientFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is RedisException or RedisTimeoutException or RedisCommandException;
    }

    /// <summary>
    /// Returns the token that classifies a server error reply: its first word after any repeated "ERR ".
    /// </summary>
    /// <param name="message">The message of the error reply.</param>
    /// <returns>The first word, or "ERR" when no word follows.</returns>
    /// <exception cref="ArgumentNullException">The message is null.</exception>
    internal static string ErrorToken(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var rest = message.AsSpan();
        while (rest.StartsWith(ErrorPrefix, StringComparison.Ordinal))
        {
            rest = rest[ErrorPrefix.Length..];
        }

        var end = rest.IndexOf(' ');
        var token = end < 0 ? rest : rest[..end];

        return token.IsEmpty ? "ERR" : token.ToString();
    }

    /// <summary>
    /// Translates a failure of the client into a coordination failure.
    /// </summary>
    /// <param name="exception">The failure of the client or the server.</param>
    /// <param name="isScript">True when the failed call ran a script, which may have applied part of its writes.</param>
    /// <returns>The coordination failure, with <paramref name="exception"/> as its inner exception.</returns>
    /// <exception cref="ArgumentNullException">The exception is null.</exception>
    public static CoordinationException Translate(Exception exception, bool isScript)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var (code, outcomeUnknown) = Classify(exception, isScript);
        var message = "The RESP server call failed: " + exception.Message;

        return new CoordinationException(code, outcomeUnknown, message, exception);
    }

    /// <summary>
    /// Picks the code and the outcome of a failure.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="isScript">True when the failed call ran a script.</param>
    /// <returns>The code and whether the call may have taken effect.</returns>
    private static (string Code, bool OutcomeUnknown) Classify(Exception exception, bool isScript)
    {
        switch (exception)
        {
            case RedisConnectionException connection:
                return connection.FailureType == ConnectionFailureType.AuthenticationFailure
                    ? (CoordinationErrorCodes.AccessDenied, false)
                    : (CoordinationErrorCodes.Unavailable, !NotSent(connection.CommandStatus));

            case RedisTimeoutException timeout:
                return (CoordinationErrorCodes.Unavailable, !NotSent(timeout.Commandstatus));

            case RedisServerException server:
                return ClassifyServerError(ErrorToken(server.Message), isScript);

            case RedisCommandException:
                return (CoordinationErrorCodes.Unavailable, false);

            default:
                return (CoordinationErrorCodes.Unavailable, true);
        }
    }

    /// <summary>
    /// Picks the code and the outcome of a server error reply from its token.
    /// </summary>
    /// <param name="token">The token of the reply.</param>
    /// <param name="isScript">True when the failed call ran a script.</param>
    /// <returns>The code and whether the call may have taken effect.</returns>
    private static (string Code, bool OutcomeUnknown) ClassifyServerError(
        string token,
        bool isScript
    )
    {
        switch (token)
        {
            case "NOPERM":
            case "NOAUTH":
            case "WRONGPASS":
                return (CoordinationErrorCodes.AccessDenied, false);

            case "LOADING":
            case "BUSY":
            case "CLUSTERDOWN":
            case "MASTERDOWN":
            case "TRYAGAIN":
            case "READONLY":
            case "CROSSSLOT":
            case "NOSCRIPT":
            case "MOVED":
            case "ASK":
                return (CoordinationErrorCodes.Unavailable, false);

            default:
                return (CoordinationErrorCodes.Unavailable, isScript);
        }
    }

    /// <summary>
    /// Returns whether the client had not yet sent the command, so it cannot have taken effect.
    /// </summary>
    /// <param name="status">The status of the command.</param>
    /// <returns>True when the command was waiting to be sent or in the backlog.</returns>
    private static bool NotSent(CommandStatus status)
    {
        return status is CommandStatus.WaitingToBeSent or CommandStatus.WaitingInBacklog;
    }
}
