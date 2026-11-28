// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Subscriptions;

namespace NCode.Coordination.PostgreSql.Notifications;

/// <summary>
/// One message of a topic's log.
/// </summary>
/// <param name="Number">The message's sequence in its topic.</param>
/// <param name="Payload">The message's payload.</param>
internal sealed record PgMessage(long Number, byte[] Payload) : IPgLogEntry;
