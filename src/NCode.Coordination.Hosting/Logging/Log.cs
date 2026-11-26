// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace NCode.Coordination.Hosting.Logging;

/// <summary>
/// The hosting's source-generated log messages. No message carries a keyspace, key or segment, since they can name a
/// tenant.
/// </summary>
internal static partial class Log
{
    /// <summary>
    /// Logs that the host opened its coordination session.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="nodeId">The node id the host joined with.</param>
    [LoggerMessage(
        EventId = EventIds.SessionOpened,
        Level = LogLevel.Information,
        Message = "The host opened its coordination session as the node {NodeId}."
    )]
    public static partial void SessionOpened(this ILogger logger, string nodeId);

    /// <summary>
    /// Logs that the host could not open its coordination session and tries again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="retryDelay">The time until the host tries again.</param>
    [LoggerMessage(
        EventId = EventIds.SessionOpenFailed,
        Level = LogLevel.Warning,
        Message = "The host could not open its coordination session; it tries again in {RetryDelay}."
    )]
    public static partial void SessionOpenFailed(
        this ILogger logger,
        Exception exception,
        TimeSpan retryDelay
    );

    /// <summary>
    /// Logs that another live session holds the node id and the host tries again.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="nodeId">The node id that is taken.</param>
    /// <param name="retryDelay">The time until the host tries again.</param>
    [LoggerMessage(
        EventId = EventIds.NodeIdTaken,
        Level = LogLevel.Warning,
        Message = "Another live session holds the node id {NodeId}; the host tries again in {RetryDelay}."
    )]
    public static partial void NodeIdTaken(this ILogger logger, string nodeId, TimeSpan retryDelay);

    /// <summary>
    /// Logs that the host could not renew its coordination session.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.KeepAliveFailed,
        Level = LogLevel.Warning,
        Message = "The host could not renew its coordination session."
    )]
    public static partial void KeepAliveFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the host's coordination session ended and the host opens a new one.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = EventIds.SessionLost,
        Level = LogLevel.Warning,
        Message = "The host's coordination session ended; the host opens a new one."
    )]
    public static partial void SessionLost(this ILogger logger);

    /// <summary>
    /// Logs that the host closed its coordination session.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = EventIds.SessionClosed,
        Level = LogLevel.Information,
        Message = "The host closed its coordination session."
    )]
    public static partial void SessionClosed(this ILogger logger);

    /// <summary>
    /// Logs that the host could not close its coordination session.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.SessionCloseFailed,
        Level = LogLevel.Warning,
        Message = "The host could not close its coordination session, which ends when its time to live passes."
    )]
    public static partial void SessionCloseFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the host's shutdown timeout passed before its coordination session closed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = EventIds.SessionCloseTimedOut,
        Level = LogLevel.Warning,
        Message = "The host's shutdown timeout passed before its coordination session closed, which ends when its time to live passes."
    )]
    public static partial void SessionCloseTimedOut(this ILogger logger);

    /// <summary>
    /// Logs that a callback registered on the end of the host's coordination session failed.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.EndedCallbackFailed,
        Level = LogLevel.Error,
        Message = "A callback registered on the end of the host's coordination session failed."
    )]
    public static partial void EndedCallbackFailed(this ILogger logger, Exception exception);

    /// <summary>
    /// Logs that the host's coordination session loop failed, so the host opens no further session until it restarts.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        EventId = EventIds.SessionLoopFailed,
        Level = LogLevel.Error,
        Message = "The host's coordination session loop failed, so the host opens no further coordination session until it restarts."
    )]
    public static partial void SessionLoopFailed(this ILogger logger, Exception exception);
}
