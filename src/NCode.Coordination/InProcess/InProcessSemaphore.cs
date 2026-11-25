// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The slots of one semaphore of the in-process provider and how many of them are held.
/// </summary>
/// <param name="capacity">The number of slots, which every acquire of the semaphore names.</param>
internal class InProcessSemaphore(int capacity)
{
    /// <summary>
    /// Gets the number of slots.
    /// </summary>
    public int Capacity { get; } = capacity;

    /// <summary>
    /// Gets the holds by slot index, with null for a slot that is free.
    /// </summary>
    public InProcessHold<SemaphoreGrant>?[] Slots { get; } =
        new InProcessHold<SemaphoreGrant>?[capacity];

    /// <summary>
    /// Gets or sets the number of slots that are held.
    /// </summary>
    public int Held { get; set; }
}
