// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// What a guarantee means: its dimension, a description and the guarantees a provider that keeps it also keeps.
/// </summary>
public sealed class GuaranteeDefinition
{
    /// <summary>
    /// Creates the definition of a guarantee.
    /// </summary>
    /// <param name="guarantee">The guarantee defined.</param>
    /// <param name="dimension">
    /// The name of the dimension the guarantee belongs to, such as <see cref="GuaranteeDimensions.Consistency"/>.
    /// </param>
    /// <param name="description">What a provider that keeps the guarantee promises, in plain words.</param>
    /// <exception cref="ArgumentNullException">The guarantee, the dimension or the description is null.</exception>
    /// <exception cref="ArgumentException">
    /// The dimension is not a valid name, or the description is empty or only white space.
    /// </exception>
    public GuaranteeDefinition(Guarantee guarantee, string dimension, string description)
    {
        ArgumentNullException.ThrowIfNull(guarantee);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Guarantee = guarantee;
        Dimension = VocabularyName.Validate(dimension, nameof(dimension));
        Description = description;
    }

    /// <summary>
    /// Gets the guarantee this definition describes.
    /// </summary>
    public Guarantee Guarantee { get; }

    /// <summary>
    /// Gets the name of the dimension the guarantee belongs to.
    /// </summary>
    public string Dimension { get; }

    /// <summary>
    /// Gets what a provider that keeps the guarantee promises, in plain words.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the guarantees a provider that keeps this one also keeps, none by default.
    /// </summary>
    /// <exception cref="ArgumentException">The list is a default array or holds a null element.</exception>
    public ImmutableArray<Guarantee> Implies
    {
        get;
        init { field = ElementList.Check(value, nameof(Implies)); }
    } = [];
}
