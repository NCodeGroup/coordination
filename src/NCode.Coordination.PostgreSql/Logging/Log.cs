// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace NCode.Coordination.PostgreSql.Logging;

/// <summary>
/// The PostgreSQL provider's source-generated log messages. No message carries a keyspace, key, segment, channel or
/// topic, since they can name a tenant.
/// </summary>
internal static partial class Log
{
    /// <summary>
    /// Logs that a whole sweep failed unexpectedly and that the next sweep runs every pass again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.SweepFailed,
        Level = LogLevel.Error,
        Message = "The sweep failed; the next sweep runs every pass again."
    )]
    public static partial void SweepFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that one pass of the sweep failed and that the next sweep runs it again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="pass">The name of the pass.</param>
    [LoggerMessage(
        EventId = EventIds.SweepPassFailed,
        Level = LogLevel.Warning,
        Message = "The sweep's {Pass} pass failed; the next sweep runs it again."
    )]
    public static partial void SweepPassFailed(
        this ILogger logger,
        Exception exception,
        string pass
    );

    /// <summary>
    /// Logs that one step of the sweep failed and that the next sweep runs it again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="step">The name of the step.</param>
    [LoggerMessage(
        EventId = EventIds.SweepStepFailed,
        Level = LogLevel.Warning,
        Message = "The sweep's {Step} step failed; the next sweep runs it again."
    )]
    public static partial void SweepStepFailed(
        this ILogger logger,
        Exception exception,
        string step
    );

    /// <summary>
    /// Logs that reading a log for its subscribers failed and that the next wake-up or check reads it again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.PumpFailed,
        Level = LogLevel.Warning,
        Message = "Reading a log for its subscribers failed; the next wake-up or check reads it again."
    )]
    public static partial void PumpFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that reading a log for its subscribers failed unexpectedly and that the next wake-up or check reads it
    /// again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.PumpFaulted,
        Level = LogLevel.Error,
        Message = "Reading a log for its subscribers failed unexpectedly; the next wake-up or check reads it again."
    )]
    public static partial void PumpFaulted(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the listening connection failed and that the listener connects again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="reconnectDelay">The time until the listener connects again.</param>
    [LoggerMessage(
        EventId = EventIds.ListenerConnectionFailed,
        Level = LogLevel.Warning,
        Message = "The listening connection failed; the listener connects again in {ReconnectDelay}."
    )]
    public static partial void ListenerConnectionFailed(
        this ILogger logger,
        Exception exception,
        TimeSpan reconnectDelay
    );

    /// <summary>
    /// Logs that disposing a broken listening connection failed, which leaves nothing to release.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.ListenerDisposeFailed,
        Level = LogLevel.Debug,
        Message = "Disposing a broken listening connection failed; it holds nothing left to release."
    )]
    public static partial void ListenerDisposeFailed(this ILogger logger, Exception exception);
}
