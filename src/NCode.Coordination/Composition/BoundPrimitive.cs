// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Composition;

/// <summary>
/// A primitive bound to a provider for a scope: the provider's implementation, if the primitive has a built-in
/// interface, and the guarantees it keeps, closed under implication.
/// </summary>
internal sealed class BoundPrimitive(
    string provider,
    Primitive primitive,
    Keyspace? keyspace,
    object? service,
    ImmutableHashSet<Guarantee> guarantees
)
{
    /// <summary>
    /// Gets the name of the provider.
    /// </summary>
    public string Provider { get; } = provider;

    /// <summary>
    /// Gets the primitive.
    /// </summary>
    public Primitive Primitive { get; } = primitive;

    /// <summary>
    /// Gets the keyspace the binding covers, or null for every keyspace.
    /// </summary>
    public Keyspace? Keyspace { get; } = keyspace;

    /// <summary>
    /// Gets the provider's implementation of the primitive's interface, or null when the primitive has none.
    /// </summary>
    public object? Service { get; } = service;

    /// <summary>
    /// Gets the guarantees the provider keeps for the primitive, with everything they imply.
    /// </summary>
    public ImmutableHashSet<Guarantee> Guarantees { get; } = guarantees;
}
