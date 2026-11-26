// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Recipes;

/// <summary>
/// The messages of the failures the recipes throw.
/// </summary>
internal static class RecipeMessages
{
    /// <summary>
    /// The message of the failure a recipe throws when a watch it waits on ends without a failure, which the contracts
    /// never do.
    /// </summary>
    public const string WatchEnded = "The watch ended without a failure.";

    /// <summary>
    /// The message of the failure a rate limiter throws when the window it counts in has already ended.
    /// </summary>
    public const string WindowEnded = "The window has already ended.";
}
