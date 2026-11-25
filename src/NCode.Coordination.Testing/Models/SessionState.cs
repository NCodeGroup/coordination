// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The state of one session in its sequential specification.
/// </summary>
/// <param name="Id">The identity the open call returned, or null before the session is open.</param>
/// <param name="Ended">True once the session has been closed.</param>
internal sealed record SessionState(SessionId? Id, bool Ended);
