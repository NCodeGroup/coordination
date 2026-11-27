// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// Reads the replies of RESP commands and Lua scripts.
/// </summary>
internal static class RespReplies
{
    /// <summary>
    /// Reads the reply of a script that returns an integer status followed by fields.
    /// </summary>
    /// <param name="reply">The reply of the script.</param>
    /// <returns>The status and the fields after it.</returns>
    /// <exception cref="ArgumentNullException">The reply is null.</exception>
    /// <exception cref="CoordinationException">The reply is not a non-empty array that starts with an integer.</exception>
    public static (long Status, RedisResult[] Fields) Script(RedisResult reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.IsNull || reply.Resp2Type != ResultType.Array)
        {
            throw Unexpected(reply);
        }

        var elements = (RedisResult[]?)reply;
        if (elements is null || elements.Length == 0)
        {
            throw Unexpected(reply);
        }

        return (Integer(elements[0]), elements[1..]);
    }

    /// <summary>
    /// Reads a reply as an integer.
    /// </summary>
    /// <param name="reply">The reply.</param>
    /// <returns>The integer, which the server sent as an integer or as the digits of a bulk string.</returns>
    /// <exception cref="ArgumentNullException">The reply is null.</exception>
    /// <exception cref="CoordinationException">The reply is not an integer.</exception>
    public static long Integer(RedisResult reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.IsNull)
        {
            throw Unexpected(reply);
        }

        switch (reply.Resp2Type)
        {
            case ResultType.Integer:
                return (long)reply;

            case ResultType.BulkString:
                var text = (string?)reply;
                if (
                    long.TryParse(
                        text,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var value
                    )
                )
                {
                    return value;
                }

                throw Unexpected(reply);

            default:
                throw Unexpected(reply);
        }
    }

    /// <summary>
    /// Reads a reply as text.
    /// </summary>
    /// <param name="reply">The reply.</param>
    /// <returns>The text, which the server sent as a bulk string or a simple string.</returns>
    /// <exception cref="ArgumentNullException">The reply is null.</exception>
    /// <exception cref="CoordinationException">The reply is null or not text.</exception>
    public static string Text(RedisResult reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (
            reply.IsNull
            || reply.Resp2Type is not (ResultType.BulkString or ResultType.SimpleString)
        )
        {
            throw Unexpected(reply);
        }

        var text = (string?)reply;
        if (text is null)
        {
            throw Unexpected(reply);
        }

        return text;
    }

    /// <summary>
    /// Creates the exception for a reply the provider does not expect.
    /// </summary>
    /// <param name="reply">The reply.</param>
    /// <returns>The exception, which says the outcome of the call is unknown, because a script may have written before it replied.</returns>
    /// <exception cref="ArgumentNullException">The reply is null.</exception>
    public static CoordinationException Unexpected(RedisResult reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var type = reply.IsNull ? "null" : reply.Resp2Type.ToString();
        return new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            outcomeUnknown: true,
            $"The RESP server answered with an unexpected reply of type {type}."
        );
    }
}
