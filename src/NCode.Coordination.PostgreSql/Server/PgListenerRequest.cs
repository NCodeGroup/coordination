// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// A <c>LISTEN</c> or <c>UNLISTEN</c> that the loop runs, and the task that awaits it.
/// </summary>
/// <param name="Sql">The statement.</param>
/// <param name="Waiter">The waiter that completes when the statement ran, or null when nobody waits.</param>
internal sealed record PgListenerRequest(string Sql, TaskCompletionSource? Waiter);
