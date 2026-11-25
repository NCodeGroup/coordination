// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A lease a provider granted: the resource, the fencing token, the owner token and the terms.
/// </summary>
/// <remarks>
/// The holder keeps the grant: renewing and releasing it need its owner token, and a resource that takes writes from
/// holders compares the fencing token.
/// </remarks>
public sealed class LeaseGrant
{
    /// <summary>
    /// Creates a lease grant.
    /// </summary>
    /// <param name="resource">The resource the lease is on.</param>
    /// <param name="fencingToken">The fencing token, greater than every earlier grant's of the resource.</param>
    /// <param name="owner">The token that identifies the holder.</param>
    /// <param name="terms">How long the grant lasts.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public LeaseGrant(
        CoordinationKey resource,
        FencingToken fencingToken,
        OwnerToken owner,
        LeaseTerms terms
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(fencingToken);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(terms);

        Resource = resource;
        FencingToken = fencingToken;
        Owner = owner;
        Terms = terms;
    }

    /// <summary>
    /// Gets the resource the lease is on.
    /// </summary>
    public CoordinationKey Resource { get; }

    /// <summary>
    /// Gets the fencing token, greater than every earlier grant's of the resource.
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
