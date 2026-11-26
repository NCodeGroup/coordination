// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A read lock a service granted: the resource, the reader's registration, its revision and the fencing token.
/// </summary>
/// <remarks>
/// The holder keeps the grant: releasing it needs the registration and its revision, and a resource that takes
/// reads or writes from holders compares the fencing token, which orders the read grant among the write grants.
/// </remarks>
public sealed class ReadLockGrant
{
    /// <summary>
    /// Creates a read lock grant.
    /// </summary>
    /// <param name="resource">The resource that is locked.</param>
    /// <param name="registration">The record that registers the reader under the resource.</param>
    /// <param name="revision">The revision of the registration record.</param>
    /// <param name="fencingToken">The fencing token that orders the grant among the write grants.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The registration is not under the resource, or it is the resource itself.
    /// </exception>
    public ReadLockGrant(
        CoordinationKey resource,
        CoordinationKey registration,
        Revision revision,
        FencingToken fencingToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(fencingToken);

        if (!resource.IsPrefixOf(registration) || resource.Equals(registration))
        {
            throw new ArgumentException(
                "The registration must be a key under the resource.",
                nameof(registration)
            );
        }

        Resource = resource;
        Registration = registration;
        Revision = revision;
        FencingToken = fencingToken;
    }

    /// <summary>
    /// Gets the resource that is locked.
    /// </summary>
    public CoordinationKey Resource { get; }

    /// <summary>
    /// Gets the record that registers the reader under the resource.
    /// </summary>
    public CoordinationKey Registration { get; }

    /// <summary>
    /// Gets the revision of the registration record.
    /// </summary>
    public Revision Revision { get; }

    /// <summary>
    /// Gets the fencing token, greater than every earlier write grant's and less than every later one's.
    /// </summary>
    public FencingToken FencingToken { get; }
}
