// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination;

/// <summary>
/// The recipes the product defines.
/// </summary>
/// <remarks>
/// A host requires a recipe with <c>RequireRecipe</c>, so its start fails on a binding that cannot keep the
/// guarantees the recipe relies on.
/// </remarks>
public static class CoordinationRecipes
{
    /// <summary>
    /// Gets the recipe of a lock: exclusive access to a resource, held through a lease.
    /// </summary>
    public static Recipe Lock { get; } =
        new("lock", new RecipeNeed(Primitives.Lease, Guarantees.LinearizableUntilFailover));

    /// <summary>
    /// Gets the recipe of a read-write lock: many readers or one writer at a time on a resource.
    /// </summary>
    public static Recipe ReadWriteLock { get; } =
        new(
            "read-write-lock",
            new RecipeNeed(Primitives.Lease, Guarantees.LinearizableUntilFailover),
            new RecipeNeed(Primitives.Record, Guarantees.LinearizableUntilFailover),
            new RecipeNeed(Primitives.RecordWatch, Guarantees.AtMostOnceWithGapDetection)
        );

    /// <summary>
    /// Gets the recipe of a barrier: callers wait until enough of them have arrived.
    /// </summary>
    public static Recipe Barrier { get; } =
        new(
            "barrier",
            new RecipeNeed(Primitives.Record, Guarantees.LinearizableUntilFailover),
            new RecipeNeed(Primitives.RecordWatch, Guarantees.AtMostOnceWithGapDetection)
        );

    /// <summary>
    /// Gets the recipe of a singleton: one node at a time runs the work, chosen by an election.
    /// </summary>
    public static Recipe Singleton { get; } =
        new("singleton", new RecipeNeed(Primitives.Election, Guarantees.LinearizableUntilFailover));

    /// <summary>
    /// Gets the recipe of partition ownership: the partitions are divided among the live members of a group.
    /// </summary>
    public static Recipe PartitionOwnership { get; } =
        new("partition-ownership", new RecipeNeed(Primitives.Membership));

    /// <summary>
    /// Gets the recipe of single-flight: one caller at a time fetches a value while the others wait for it.
    /// </summary>
    public static Recipe SingleFlight { get; } =
        new(
            "single-flight",
            new RecipeNeed(Primitives.Record, Guarantees.LinearizableUntilFailover),
            new RecipeNeed(Primitives.Lease, Guarantees.LinearizableUntilFailover)
        );

    /// <summary>
    /// Gets the recipe of a fixed-window rate limiter: a limited number of permits in each fixed window of time.
    /// </summary>
    public static Recipe FixedWindowRateLimiter { get; } =
        new(
            "fixed-window-rate-limiter",
            new RecipeNeed(Primitives.Counter, Guarantees.LinearizableUntilFailover)
        );

    /// <summary>
    /// Gets the recipe of a sliding-window rate limiter: a limited number of permits in a window that slides.
    /// </summary>
    public static Recipe SlidingWindowRateLimiter { get; } =
        new(
            "sliding-window-rate-limiter",
            new RecipeNeed(Primitives.Counter, Guarantees.LinearizableUntilFailover)
        );

    /// <summary>
    /// Gets the recipe of a token-bucket rate limiter: permits that refill at a steady rate up to a limit.
    /// </summary>
    public static Recipe TokenBucketRateLimiter { get; } =
        new(
            "token-bucket-rate-limiter",
            new RecipeNeed(Primitives.Record, Guarantees.LinearizableUntilFailover)
        );

    /// <summary>
    /// Gets the recipe of cache invalidation: every node that holds a cache learns which entries to drop.
    /// </summary>
    public static Recipe CacheInvalidation { get; } =
        new(
            "cache-invalidation",
            new RecipeNeed(Primitives.Notification, Guarantees.AtMostOnceWithGapDetection)
        );

    /// <summary>
    /// Gets every built-in recipe.
    /// </summary>
    public static ImmutableArray<Recipe> All { get; } =
    [
        Lock,
        ReadWriteLock,
        Barrier,
        Singleton,
        PartitionOwnership,
        SingleFlight,
        FixedWindowRateLimiter,
        SlidingWindowRateLimiter,
        TokenBucketRateLimiter,
        CacheInvalidation,
    ];
}
