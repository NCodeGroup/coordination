// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of trying to acquire a semaphore slot.
/// </summary>
public enum SemaphoreAcquireStatus
{
    /// <summary>
    /// A slot was acquired.
    /// </summary>
    Acquired,

    /// <summary>
    /// Every slot is held.
    /// </summary>
    Full,

    /// <summary>
    /// The current holders use another capacity.
    /// </summary>
    CapacityConflict,

    /// <summary>
    /// The session the slot was to be bound to has ended.
    /// </summary>
    SessionEnded,
}
