// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Requires a recipe's primitives and guarantees on an application's coordination builder.
/// </summary>
public static class RecipeCoordinationBuilderExtensions
{
    /// <summary>
    /// Adds one requirement of a feature for each primitive a recipe uses.
    /// </summary>
    /// <param name="builder">The builder to add the requirements to.</param>
    /// <param name="recipe">The recipe whose needs become requirements.</param>
    /// <param name="feature">The name of the feature that uses the recipe.</param>
    /// <param name="keyspace">The keyspace the feature uses, or null for the composition's default binding.</param>
    /// <returns>The builder, for chaining.</returns>
    private static ICoordinationBuilder RequireNeeds(
        ICoordinationBuilder builder,
        Recipe recipe,
        string feature,
        Keyspace? keyspace
    )
    {
        // Every requirement is built before any is added, so an invalid feature name adds nothing.
        var requirements = new List<FeatureRequirement>(recipe.Needs.Length);
        foreach (var need in recipe.Needs)
        {
            requirements.Add(
                new FeatureRequirement(feature, need.Primitive, need.Guarantees)
                {
                    Keyspace = keyspace,
                }
            );
        }

        foreach (var requirement in requirements)
        {
            builder.Require(requirement);
        }

        return builder;
    }

    extension(ICoordinationBuilder builder)
    {
        /// <summary>
        /// Requires what a recipe relies on for a feature, on the composition's default binding.
        /// </summary>
        /// <param name="recipe">The recipe the feature uses.</param>
        /// <param name="feature">The name of the feature that uses the recipe.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">The builder, the recipe or the feature are null.</exception>
        /// <exception cref="ArgumentException">The feature is not a valid name.</exception>
        public ICoordinationBuilder RequireRecipe(Recipe recipe, string feature)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(recipe);

            return RequireNeeds(builder, recipe, feature, null);
        }

        /// <summary>
        /// Requires what a recipe relies on for a feature, on the bindings of a keyspace.
        /// </summary>
        /// <param name="recipe">The recipe the feature uses.</param>
        /// <param name="feature">The name of the feature that uses the recipe.</param>
        /// <param name="keyspace">The keyspace the feature uses.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// The builder, the recipe, the feature or the keyspace are null.
        /// </exception>
        /// <exception cref="ArgumentException">The feature is not a valid name.</exception>
        public ICoordinationBuilder RequireRecipe(Recipe recipe, string feature, Keyspace keyspace)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(recipe);
            ArgumentNullException.ThrowIfNull(keyspace);

            return RequireNeeds(builder, recipe, feature, keyspace);
        }
    }
}
