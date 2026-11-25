// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class GuaranteesTests
{
    private static Guarantee[] All { get; } =
    [
        Guarantees.LinearizableAcrossFailover,
        Guarantees.LinearizableUntilFailover,
        Guarantees.LinearizableWithinProcess,
        Guarantees.Durable,
        Guarantees.ReplicatedAsynchronously,
        Guarantees.Volatile,
        Guarantees.ServerClock,
        Guarantees.CallerClock,
        Guarantees.AtMostOnceWithGapDetection,
        Guarantees.AtMostOnce,
    ];

    private static Guarantee[] Weakest { get; } =
    [
        Guarantees.LinearizableWithinProcess,
        Guarantees.Volatile,
        Guarantees.CallerClock,
        Guarantees.AtMostOnce,
    ];

    private static GuaranteeDefinition DefinitionOf(Guarantee guarantee) =>
        Assert.Single(Guarantees.Definitions, definition => definition.Guarantee == guarantee);

    #region Definitions Property Tests

    [Fact]
    public void Definitions_Always_HoldsEachBuiltInGuaranteeOnceInOrder()
    {
        Guarantee[] actual = [.. Guarantees.Definitions.Select(d => d.Guarantee)];
        var distinct = All.Distinct().Count();

        Assert.Equal<Guarantee>(All, actual);
        Assert.Equal(All.Length, distinct);
    }

    [Fact]
    public void Definitions_Always_NamesEachGuaranteeAfterItsDimension()
    {
        foreach (var definition in Guarantees.Definitions)
        {
            Assert.StartsWith(definition.Dimension + ".", definition.Guarantee.Name);
        }
    }

    [Fact]
    public void Definitions_Always_UsesTheKnownDimensions()
    {
        string[] dimensions =
        [
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry,
            GuaranteeDimensions.Delivery,
        ];

        Assert.All(Guarantees.Definitions, d => Assert.Contains(d.Dimension, dimensions));
    }

    [Fact]
    public void Definitions_Always_HasADescriptionForEachGuarantee()
    {
        foreach (var definition in Guarantees.Definitions)
        {
            var hasDescription = !string.IsNullOrWhiteSpace(definition.Description);

            Assert.True(hasDescription);
        }
    }

    [Fact]
    public void Definitions_Always_ImpliesOnlyDefinedGuarantees()
    {
        foreach (var implied in Guarantees.Definitions.SelectMany(d => d.Implies))
        {
            Assert.Contains(implied, All);
        }
    }

    [Fact]
    public void Definitions_WhenLinearizableAcrossFailover_ImpliesLinearizableUntilFailover()
    {
        var implies = DefinitionOf(Guarantees.LinearizableAcrossFailover).Implies;

        Assert.Equal<Guarantee>([Guarantees.LinearizableUntilFailover], implies);
    }

    [Fact]
    public void Definitions_WhenLinearizableUntilFailover_ImpliesLinearizableWithinProcess()
    {
        var implies = DefinitionOf(Guarantees.LinearizableUntilFailover).Implies;

        Assert.Equal<Guarantee>([Guarantees.LinearizableWithinProcess], implies);
    }

    [Fact]
    public void Definitions_WhenDurable_ImpliesVolatile()
    {
        var implies = DefinitionOf(Guarantees.Durable).Implies;

        Assert.Equal<Guarantee>([Guarantees.Volatile], implies);
    }

    [Fact]
    public void Definitions_WhenReplicatedAsynchronously_ImpliesVolatile()
    {
        var implies = DefinitionOf(Guarantees.ReplicatedAsynchronously).Implies;

        Assert.Equal<Guarantee>([Guarantees.Volatile], implies);
    }

    [Fact]
    public void Definitions_WhenServerClock_ImpliesCallerClock()
    {
        var implies = DefinitionOf(Guarantees.ServerClock).Implies;

        Assert.Equal<Guarantee>([Guarantees.CallerClock], implies);
    }

    [Fact]
    public void Definitions_WhenAtMostOnceWithGapDetection_ImpliesAtMostOnce()
    {
        var implies = DefinitionOf(Guarantees.AtMostOnceWithGapDetection).Implies;

        Assert.Equal<Guarantee>([Guarantees.AtMostOnce], implies);
    }

    [Fact]
    public void Definitions_WhenGuaranteeIsWeakest_ImpliesNothing()
    {
        foreach (var guarantee in Weakest)
        {
            var implies = DefinitionOf(guarantee).Implies;

            Assert.Empty(implies);
        }
    }

    [Fact]
    public void Definitions_WhenDurableAndReplicatedAsynchronously_AreIncomparable()
    {
        var durableImplies = DefinitionOf(Guarantees.Durable).Implies;
        var replicatedImplies = DefinitionOf(Guarantees.ReplicatedAsynchronously).Implies;

        Assert.DoesNotContain(Guarantees.ReplicatedAsynchronously, durableImplies);
        Assert.DoesNotContain(Guarantees.Durable, replicatedImplies);
    }

    #endregion
}
