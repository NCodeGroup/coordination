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

    #region Definitions Tests

    [Fact]
    public void Definitions_Always_HoldsEachBuiltInGuaranteeOnceInOrder()
    {
        Assert.Equal<Guarantee>(All, [.. Guarantees.Definitions.Select(d => d.Guarantee)]);
        Assert.Equal(All.Length, All.Distinct().Count());
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
        Assert.All(Guarantees.Definitions, definition =>
            Assert.False(string.IsNullOrWhiteSpace(definition.Description))
        );
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
        Assert.Equal<Guarantee>(
            [Guarantees.LinearizableUntilFailover],
            DefinitionOf(Guarantees.LinearizableAcrossFailover).Implies
        );
    }

    [Fact]
    public void Definitions_WhenLinearizableUntilFailover_ImpliesLinearizableWithinProcess()
    {
        Assert.Equal<Guarantee>(
            [Guarantees.LinearizableWithinProcess],
            DefinitionOf(Guarantees.LinearizableUntilFailover).Implies
        );
    }

    [Fact]
    public void Definitions_WhenDurable_ImpliesVolatile()
    {
        Assert.Equal<Guarantee>([Guarantees.Volatile], DefinitionOf(Guarantees.Durable).Implies);
    }

    [Fact]
    public void Definitions_WhenReplicatedAsynchronously_ImpliesVolatile()
    {
        Assert.Equal<Guarantee>(
            [Guarantees.Volatile],
            DefinitionOf(Guarantees.ReplicatedAsynchronously).Implies
        );
    }

    [Fact]
    public void Definitions_WhenServerClock_ImpliesCallerClock()
    {
        Assert.Equal<Guarantee>(
            [Guarantees.CallerClock],
            DefinitionOf(Guarantees.ServerClock).Implies
        );
    }

    [Fact]
    public void Definitions_WhenAtMostOnceWithGapDetection_ImpliesAtMostOnce()
    {
        Assert.Equal<Guarantee>(
            [Guarantees.AtMostOnce],
            DefinitionOf(Guarantees.AtMostOnceWithGapDetection).Implies
        );
    }

    [Fact]
    public void Definitions_WhenGuaranteeIsWeakest_ImpliesNothing()
    {
        Assert.All(Weakest, guarantee => Assert.Empty(DefinitionOf(guarantee).Implies));
    }

    [Fact]
    public void Definitions_WhenDurableAndReplicatedAsynchronously_AreIncomparable()
    {
        Assert.DoesNotContain(
            Guarantees.ReplicatedAsynchronously,
            DefinitionOf(Guarantees.Durable).Implies
        );
        Assert.DoesNotContain(
            Guarantees.Durable,
            DefinitionOf(Guarantees.ReplicatedAsynchronously).Implies
        );
    }

    #endregion
}
