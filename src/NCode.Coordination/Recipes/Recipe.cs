// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// A recipe over the primitives: its name, and each primitive it uses with the guarantees it relies on there.
/// </summary>
public sealed class Recipe
{
    /// <summary>
    /// Creates a recipe.
    /// </summary>
    /// <param name="name">The name of the recipe.</param>
    /// <param name="needs">The primitives the recipe uses, each named once, with the guarantees it relies on.</param>
    /// <exception cref="ArgumentNullException">The name or the needs are null.</exception>
    /// <exception cref="ArgumentException">
    /// The name is empty or white space, the needs are empty, a need is null, or two needs name the same primitive.
    /// </exception>
    public Recipe(string name, params IEnumerable<RecipeNeed> needs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(needs);

        var copy = needs.ToImmutableArray();
        if (copy.IsEmpty)
        {
            throw new ArgumentException("A recipe needs at least one primitive.", nameof(needs));
        }

        var primitives = new HashSet<Primitive>();
        foreach (var need in copy)
        {
            if (need is null)
            {
                throw new ArgumentException("A need is null.", nameof(needs));
            }

            if (!primitives.Add(need.Primitive))
            {
                throw new ArgumentException("A recipe names each primitive once.", nameof(needs));
            }
        }

        Name = name;
        Needs = copy;
    }

    /// <summary>
    /// Gets the name of the recipe.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the primitives the recipe uses, in order, each with the guarantees it relies on there.
    /// </summary>
    public ImmutableArray<RecipeNeed> Needs { get; }

    /// <summary>
    /// Returns the name of the recipe.
    /// </summary>
    /// <returns>The name of the recipe.</returns>
    public override string ToString() => Name;
}
