// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Hosting.Logging;

/// <summary>
/// Names the event ids of the hosting's log messages, which are append-only.
/// </summary>
internal static class EventIds
{
    /// <summary>
    /// The first id of the hosting's band, which spans 2000 to 2999.
    /// </summary>
    public const int Base = 2000;

    /// <summary>
    /// The host opened its coordination session.
    /// </summary>
    public const int SessionOpened = Base + 1;

    /// <summary>
    /// The host could not open its coordination session.
    /// </summary>
    public const int SessionOpenFailed = Base + 2;

    /// <summary>
    /// Another live session holds the node id.
    /// </summary>
    public const int NodeIdTaken = Base + 3;

    /// <summary>
    /// The host could not renew its coordination session.
    /// </summary>
    public const int KeepAliveFailed = Base + 4;

    /// <summary>
    /// The host's coordination session ended.
    /// </summary>
    public const int SessionLost = Base + 5;

    /// <summary>
    /// The host closed its coordination session.
    /// </summary>
    public const int SessionClosed = Base + 6;

    /// <summary>
    /// The host could not close its coordination session.
    /// </summary>
    public const int SessionCloseFailed = Base + 7;

    /// <summary>
    /// The host's shutdown timeout passed before its coordination session closed.
    /// </summary>
    public const int SessionCloseTimedOut = Base + 8;

    /// <summary>
    /// A callback registered on the end of the host's coordination session failed.
    /// </summary>
    public const int EndedCallbackFailed = Base + 9;

    /// <summary>
    /// The host's coordination session loop failed and ended.
    /// </summary>
    public const int SessionLoopFailed = Base + 10;
}
