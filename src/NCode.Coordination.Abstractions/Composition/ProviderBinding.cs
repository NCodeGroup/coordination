// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// Which primitives a provider serves, for every keyspace or for one keyspace and the keyspaces under it.
/// </summary>
public sealed class ProviderBinding
{
    /// <summary>
    /// Creates the binding of a provider.
    /// </summary>
    /// <param name="provider">The name of the provider, as its declaration names it.</param>
    /// <param name="primitives">
    /// The primitives the provider serves, possibly none, which means every primitive the provider supports.
    /// </param>
    /// <exception cref="ArgumentNullException">The provider or the primitives are null.</exception>
    /// <exception cref="ArgumentException">
    /// The provider is not a valid name, a primitive is null, or a primitive is listed twice.
    /// </exception>
    public ProviderBinding(string provider, params IEnumerable<Primitive> primitives)
    {
        Provider = VocabularyName.Validate(provider, nameof(provider));
        Primitives = ElementList.Copy(primitives, nameof(primitives));

        HashSet<Primitive> seen = [];
        foreach (var primitive in Primitives)
        {
            if (!seen.Add(primitive))
            {
                throw new ArgumentException(
                    "A binding lists each primitive once.",
                    nameof(primitives)
                );
            }
        }
    }

    /// <summary>
    /// Gets the name of the provider.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets the primitives the provider serves; none means every primitive the provider supports.
    /// </summary>
    public ImmutableArray<Primitive> Primitives { get; }

    /// <summary>
    /// Gets the keyspace the binding covers, with every keyspace of the same cluster whose segments start with its
    /// segments, or null for every keyspace, which is the default.
    /// </summary>
    public Keyspace? Keyspace { get; init; }
}
