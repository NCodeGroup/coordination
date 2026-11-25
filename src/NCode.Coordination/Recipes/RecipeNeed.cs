// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// One primitive a recipe uses, and the guarantees it relies on there.
/// </summary>
public sealed class RecipeNeed
{
    /// <summary>
    /// Creates a need of a recipe.
    /// </summary>
    /// <param name="primitive">The primitive the recipe uses.</param>
    /// <param name="guarantees">
    /// The guarantees the recipe relies on in the primitive, possibly none, which asks for the primitive with no
    /// particular guarantee.
    /// </param>
    /// <exception cref="ArgumentNullException">The primitive or the guarantees are null.</exception>
    /// <exception cref="ArgumentException">A guarantee is null.</exception>
    public RecipeNeed(Primitive primitive, params IEnumerable<Guarantee> guarantees)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentNullException.ThrowIfNull(guarantees);

        var copy = guarantees.ToImmutableArray();
        foreach (var guarantee in copy)
        {
            if (guarantee is null)
            {
                throw new ArgumentException("A guarantee is null.", nameof(guarantees));
            }
        }

        Primitive = primitive;
        Guarantees = copy;
    }

    /// <summary>
    /// Gets the primitive the recipe uses.
    /// </summary>
    public Primitive Primitive { get; }

    /// <summary>
    /// Gets the guarantees the recipe relies on in the primitive, none when it needs no particular guarantee.
    /// </summary>
    public ImmutableArray<Guarantee> Guarantees { get; }
}
