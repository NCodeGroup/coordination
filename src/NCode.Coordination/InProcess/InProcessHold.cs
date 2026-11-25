// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The ownership of a resource or slot that the in-process provider has granted.
/// </summary>
/// <typeparam name="TGrant">The type of the grant that was handed to the holder.</typeparam>
/// <param name="grant">The grant that was handed to the holder.</param>
/// <param name="owner">The owner token of the grant.</param>
/// <param name="terms">How long the grant lasts.</param>
internal class InProcessHold<TGrant>(TGrant grant, OwnerToken owner, LeaseTerms terms)
    where TGrant : class
{
    /// <summary>
    /// Gets the grant that was handed to the holder.
    /// </summary>
    public TGrant Grant { get; } = grant;

    /// <summary>
    /// Gets the owner token of the grant.
    /// </summary>
    public OwnerToken Owner { get; } = owner;

    /// <summary>
    /// Gets how long the grant lasts.
    /// </summary>
    public LeaseTerms Terms { get; } = terms;

    /// <summary>
    /// Gets or sets the deadline that ends the grant, or null when the grant is bound to a session.
    /// </summary>
    public InProcessDeadline? Deadline { get; set; }
}
