// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class CoordinationRecipesTests
{
    private static string DescribeNeeds(Recipe recipe)
    {
        var parts = new List<string>();
        foreach (var need in recipe.Needs)
        {
            var guarantees = need.Guarantees.Select(guarantee => guarantee.Name);
            var names = string.Join(",", guarantees);
            parts.Add($"{need.Primitive.Name}[{names}]");
        }

        return string.Join(" ", parts);
    }

    #region All Property Tests

    [Fact]
    public void All_Always_ListsTheTenRecipesInOrder()
    {
        string[] names =
        [
            "lock",
            "read-write-lock",
            "barrier",
            "singleton",
            "partition-ownership",
            "single-flight",
            "fixed-window-rate-limiter",
            "sliding-window-rate-limiter",
            "token-bucket-rate-limiter",
            "cache-invalidation",
        ];
        Recipe[] recipes =
        [
            CoordinationRecipes.Lock,
            CoordinationRecipes.ReadWriteLock,
            CoordinationRecipes.Barrier,
            CoordinationRecipes.Singleton,
            CoordinationRecipes.PartitionOwnership,
            CoordinationRecipes.SingleFlight,
            CoordinationRecipes.FixedWindowRateLimiter,
            CoordinationRecipes.SlidingWindowRateLimiter,
            CoordinationRecipes.TokenBucketRateLimiter,
            CoordinationRecipes.CacheInvalidation,
        ];

        var all = CoordinationRecipes.All;
        var actualNames = all.Select(recipe => recipe.Name);

        Assert.Equal(names, actualNames);
        Assert.Equal(recipes.Length, all.Length);
        for (var index = 0; index < recipes.Length; index++)
        {
            Assert.Same(recipes[index], all[index]);
        }
    }

    [Fact]
    public void All_Always_HoldsEachRecipesNeedsAsTheDesignListsThem()
    {
        string[] expected =
        [
            "lock: lease[consistency.linearizable-until-failover]",
            "read-write-lock: lease[consistency.linearizable-until-failover] record[consistency.linearizable-until-failover] record-watch[delivery.at-most-once-with-gap-detection]",
            "barrier: record[consistency.linearizable-until-failover] record-watch[delivery.at-most-once-with-gap-detection]",
            "singleton: election[consistency.linearizable-until-failover]",
            "partition-ownership: membership[]",
            "single-flight: record[consistency.linearizable-until-failover] lease[consistency.linearizable-until-failover]",
            "fixed-window-rate-limiter: counter[consistency.linearizable-until-failover]",
            "sliding-window-rate-limiter: counter[consistency.linearizable-until-failover]",
            "token-bucket-rate-limiter: record[consistency.linearizable-until-failover]",
            "cache-invalidation: notification[delivery.at-most-once-with-gap-detection]",
        ];
        var actual = new List<string>();

        foreach (var recipe in CoordinationRecipes.All)
        {
            var needs = DescribeNeeds(recipe);
            actual.Add($"{recipe.Name}: {needs}");
        }

        Assert.Equal(expected, actual);
    }

    #endregion
}
