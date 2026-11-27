// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// A signal that a channel reader receives: a message, a due check, or a connection event.
/// </summary>
/// <param name="Kind">The kind of the signal.</param>
/// <param name="Message">The message of a <see cref="RespSignalKind.Message" /> signal, otherwise null.</param>
internal readonly record struct RespSignal(RespSignalKind Kind, RedisValue Message)
{
    /// <summary>
    /// Gets the signal that the periodic check is due.
    /// </summary>
    public static RespSignal Check { get; } = new(RespSignalKind.Check, RedisValue.Null);

    /// <summary>
    /// Gets the signal that a connection of the client failed.
    /// </summary>
    public static RespSignal Failed { get; } = new(RespSignalKind.Failed, RedisValue.Null);

    /// <summary>
    /// Gets the signal that a connection of the client was restored.
    /// </summary>
    public static RespSignal Restored { get; } = new(RespSignalKind.Restored, RedisValue.Null);

    /// <summary>
    /// Creates the signal for a message that the channel carried.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The signal.</returns>
    public static RespSignal ForMessage(RedisValue message) => new(RespSignalKind.Message, message);
}
