// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// An item that a channel reader yields: a message in order, or a resync.
/// </summary>
/// <param name="IsResync">True when the item is a resync, false when it is a message.</param>
/// <param name="Number">The message's number, or the channel's number that a resync starts from.</param>
/// <param name="Message">The message of a message item, otherwise null.</param>
internal readonly record struct RespChannelItem(bool IsResync, long Number, RedisValue Message)
{
    /// <summary>
    /// Creates the item for a resync.
    /// </summary>
    /// <param name="number">The channel's number that the resync starts from.</param>
    /// <returns>The item.</returns>
    public static RespChannelItem ForResync(long number) => new(true, number, RedisValue.Null);

    /// <summary>
    /// Creates the item for a message in order.
    /// </summary>
    /// <param name="number">The message's number.</param>
    /// <param name="message">The message.</param>
    /// <returns>The item.</returns>
    public static RespChannelItem ForMessage(long number, RedisValue message) =>
        new(false, number, message);
}
