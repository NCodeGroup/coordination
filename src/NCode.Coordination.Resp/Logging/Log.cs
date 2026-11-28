// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace NCode.Coordination.Resp.Logging;

/// <summary>
/// The RESP provider's source-generated log messages. No message carries a keyspace, key, segment, channel or topic,
/// since they can name a tenant.
/// </summary>
internal static partial class Log
{
    /// <summary>
    /// Logs that a subscription could not read its channel's number and resyncs at a later check.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.ChannelNumberReadFailed,
        Level = LogLevel.Warning,
        Message = "A subscription could not read its channel's number; it stays in doubt and resyncs at a later check."
    )]
    public static partial void ChannelNumberReadFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that a record watch could not read a written record and resyncs at a later check.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.WatchRecordReadFailed,
        Level = LogLevel.Warning,
        Message = "A record watch could not read a written record; it resyncs at a later check."
    )]
    public static partial void WatchRecordReadFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that a membership watch could not read its group and resyncs at a later check.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.WatchGroupReadFailed,
        Level = LogLevel.Warning,
        Message = "A membership watch could not read its group; it resyncs at a later check."
    )]
    public static partial void WatchGroupReadFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that unsubscribing from a channel failed and that the subscription ends with the connection.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.UnsubscribeFailed,
        Level = LogLevel.Debug,
        Message = "Unsubscribing from a channel failed; the subscription ends with the connection."
    )]
    public static partial void UnsubscribeFailed(this ILogger logger, Exception exception);
}
