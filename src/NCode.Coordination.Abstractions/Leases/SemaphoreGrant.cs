// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A semaphore slot a provider granted: the resource, the slot, the slot's fencing token, the owner token and the
/// terms.
/// </summary>
/// <remarks>
/// The holder keeps the grant: renewing and releasing it need its owner token, and a resource that takes writes from
/// holders compares the fencing token of the slot.
/// </remarks>
public sealed class SemaphoreGrant
{
    /// <summary>
    /// Creates a semaphore grant.
    /// </summary>
    /// <param name="resource">The resource the semaphore is on.</param>
    /// <param name="slot">The index of the slot, from 0 to the capacity less one.</param>
    /// <param name="fencingToken">The fencing token, greater than every earlier grant's of the slot.</param>
    /// <param name="owner">The token that identifies the holder.</param>
    /// <param name="terms">How long the grant lasts.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The slot is negative or not below the largest capacity.</exception>
    public SemaphoreGrant(
        CoordinationKey resource,
        int slot,
        FencingToken fencingToken,
        OwnerToken owner,
        LeaseTerms terms
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            slot,
            CoordinationLimits.MaxSemaphoreCapacity
        );
        ArgumentNullException.ThrowIfNull(fencingToken);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(terms);

        Resource = resource;
        Slot = slot;
        FencingToken = fencingToken;
        Owner = owner;
        Terms = terms;
    }

    /// <summary>
    /// Gets the resource the semaphore is on.
    /// </summary>
    public CoordinationKey Resource { get; }

    /// <summary>
    /// Gets the index of the slot, from 0 to the capacity less one, whose fencing token this grant carries.
    /// </summary>
    public int Slot { get; }

    /// <summary>
    /// Gets the fencing token, greater than every earlier grant's of the slot.
    /// </summary>
    public FencingToken FencingToken { get; }

    /// <summary>
    /// Gets the token that identifies the holder and that renewing and releasing the grant need.
    /// </summary>
    public OwnerToken Owner { get; }

    /// <summary>
    /// Gets how long the grant lasts.
    /// </summary>
    public LeaseTerms Terms { get; }
}
