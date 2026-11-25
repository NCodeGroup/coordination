// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of trying to acquire a lease.
/// </summary>
public enum LeaseAcquireStatus
{
    /// <summary>
    /// The lease was acquired.
    /// </summary>
    Acquired,

    /// <summary>
    /// Another holder has the lease.
    /// </summary>
    Held,

    /// <summary>
    /// The session the lease was to be bound to has ended.
    /// </summary>
    SessionEnded,
}
