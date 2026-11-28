// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Notifications;

/// <summary>
/// Holds what a publish needs besides the keyspace's id.
/// </summary>
/// <param name="Index">The topic's index value.</param>
/// <param name="Depth">The topic's partition depth.</param>
/// <param name="Text">The topic's key as text.</param>
/// <param name="Body">The message's payload.</param>
/// <param name="Retention">How long the message is kept, in microseconds.</param>
/// <param name="Announcement">The start of the wake-up payload, which the message's sequence completes.</param>
internal sealed record PgPublishRequest(
    byte[] Index,
    short Depth,
    string Text,
    byte[] Body,
    long Retention,
    string Announcement
);
