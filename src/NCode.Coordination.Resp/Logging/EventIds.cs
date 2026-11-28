// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Logging;

/// <summary>
/// Names the event ids of the RESP provider's log messages, which are append-only.
/// </summary>
internal static class EventIds
{
    /// <summary>
    /// The first id of the RESP provider's band, which spans 3000 to 3999.
    /// </summary>
    public const int Base = 3000;

    /// <summary>
    /// A subscription could not read its channel's number.
    /// </summary>
    public const int ChannelNumberReadFailed = Base + 1;

    /// <summary>
    /// A record watch could not read a written record.
    /// </summary>
    public const int WatchRecordReadFailed = Base + 2;

    /// <summary>
    /// A membership watch could not read its group.
    /// </summary>
    public const int WatchGroupReadFailed = Base + 3;

    /// <summary>
    /// Unsubscribing from a channel failed as a subscription ended.
    /// </summary>
    public const int UnsubscribeFailed = Base + 4;
}
