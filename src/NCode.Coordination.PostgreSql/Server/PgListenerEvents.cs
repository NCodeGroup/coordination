// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Holds what a listener calls as its connection receives notifications, is replaced and reaches another server.
/// </summary>
/// <param name="Notified">Called with the channel and the payload of every notification.</param>
/// <param name="Reconnected">Called after every connection but the first has listened to every referenced channel.</param>
/// <param name="IdentityChanged">Called when a connection reaches a server that started at another time.</param>
internal sealed record PgListenerEvents(
    Action<string, string> Notified,
    Action Reconnected,
    Action IdentityChanged
);
