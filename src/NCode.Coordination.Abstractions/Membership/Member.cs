// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// A node in a membership group: the session that keeps it alive, its identity, its address and its metadata.
/// </summary>
public sealed class Member
{
    /// <summary>
    /// Creates a member.
    /// </summary>
    /// <param name="session">The session that keeps the member alive.</param>
    /// <param name="nodeId">The identity of the node, a token.</param>
    /// <param name="address">The address of the node, such as <c>10.0.0.4:5000</c>.</param>
    /// <exception cref="ArgumentNullException">The session or the node id is null.</exception>
    /// <exception cref="ArgumentException">
    /// The node id is not a token, or the address is empty, whitespace or longer than the limit.
    /// </exception>
    public Member(SessionId session, string nodeId, string address)
    {
        ArgumentNullException.ThrowIfNull(session);
        NodeId = OpaqueToken.Validate(nodeId, CoordinationLimits.MaxTokenLength, nameof(nodeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        if (address.Length > CoordinationLimits.MaxAddressLength)
        {
            throw new ArgumentException(
                $"An address has at most {CoordinationLimits.MaxAddressLength} characters.",
                nameof(address)
            );
        }

        Session = session;
        Address = address;
    }

    /// <summary>
    /// Gets the session that keeps the member alive.
    /// </summary>
    public SessionId Session { get; }

    /// <summary>
    /// Gets the identity of the node.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// Gets the address of the node, text the provider stores as written, such as <c>10.0.0.4:5000</c>.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the metadata of the node, each a provider-prefixed name with a text value, none by default.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// There are too many entries, a name is not a valid name, or a value is null or too long.
    /// </exception>
    public ImmutableDictionary<string, string> Metadata
    {
        get;
        init
        {
            field = NamedValues.Check(
                value,
                CoordinationLimits.MaxMetadataEntries,
                CoordinationLimits.MaxMetadataValueLength,
                nameof(Metadata)
            );
        }
    } = ImmutableDictionary<string, string>.Empty;
}
