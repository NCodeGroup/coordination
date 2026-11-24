// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// What a provider keeps for each primitive it lists: the guarantees it holds, or the reason it does not support the
/// primitive.
/// </summary>
/// <remarks>
/// A primitive the provider does not list is one it does not support.
/// </remarks>
public sealed class ProviderDeclaration
{
    /// <summary>
    /// Creates the declaration of a provider.
    /// </summary>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="primitives">What the provider offers for each primitive it lists, possibly none.</param>
    /// <exception cref="ArgumentNullException">The provider or the primitives are null.</exception>
    /// <exception cref="ArgumentException">The provider is not a valid name, or a primitive support is null.</exception>
    public ProviderDeclaration(string provider, params IEnumerable<PrimitiveSupport> primitives)
    {
        Provider = VocabularyName.Validate(provider, nameof(provider));
        Primitives = ElementList.Copy(primitives, nameof(primitives));
    }

    /// <summary>
    /// Gets the name of the provider.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets what the provider offers for each primitive it lists.
    /// </summary>
    public ImmutableArray<PrimitiveSupport> Primitives { get; }

    /// <summary>
    /// Gets the name of the vendor profile the provider applied, or null when it applied none, which is the default.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a valid name.</exception>
    public string? Profile
    {
        get;
        init { field = value is null ? null : VocabularyName.Validate(value, nameof(Profile)); }
    }

    /// <summary>
    /// Gets the guarantees the provider defines beyond the built-in ones, none by default.
    /// </summary>
    /// <exception cref="ArgumentException">The list is a default array or holds a null element.</exception>
    public ImmutableArray<GuaranteeDefinition> Definitions
    {
        get;
        init { field = ElementList.Check(value, nameof(Definitions)); }
    } = [];
}
