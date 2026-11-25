// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition.Tests;

public sealed class GuaranteeRegistryTests
{
    private static Guarantee A { get; } = new("test.a");

    private static Guarantee B { get; } = new("test.b");

    private static Guarantee C { get; } = new("test.c");

    #region Create Tests

    [Fact]
    public void Create_WhenDefinitionsAreDistinct_DefinesEach()
    {
        var failures = new List<string>();
        GuaranteeDefinition[] definitions = [new(A, "test", "a"), new(B, "test", "b")];

        var registry = GuaranteeRegistry.Create(definitions, failures);

        Assert.Empty(failures);
        var isDefinedA = registry.IsDefined(A);
        Assert.True(isDefinedA);
        var isDefinedB = registry.IsDefined(B);
        Assert.True(isDefinedB);
        var isDefinedC = registry.IsDefined(C);
        Assert.False(isDefinedC);
    }

    [Fact]
    public void Create_WhenANameIsDefinedTwiceTheSameWay_DefinesItOnceWithoutFailure()
    {
        var failures = new List<string>();
        GuaranteeDefinition[] definitions =
        [
            new(A, "test", "a") { Implies = [B] },
            new(B, "test", "b"),
            new(A, "test", "a") { Implies = [B] },
        ];

        var registry = GuaranteeRegistry.Create(definitions, failures);

        Assert.Empty(failures);
        var isDefinedA = registry.IsDefined(A);
        Assert.True(isDefinedA);
    }

    [Fact]
    public void Create_WhenANameIsDefinedTwiceDifferently_Fails()
    {
        var failures = new List<string>();
        GuaranteeDefinition[] definitions = [new(A, "test", "a"), new(A, "test", "other")];

        var registry = GuaranteeRegistry.Create(definitions, failures);

        Assert.Equal(
            ["The guarantee 'test.a' is defined twice, with different meanings."],
            failures
        );
        var isDefinedA = registry.IsDefined(A);
        Assert.True(isDefinedA);
    }

    [Fact]
    public void Create_WhenAnImplicationIsUndefined_Fails()
    {
        var failures = new List<string>();
        GuaranteeDefinition[] definitions = [new(A, "test", "a") { Implies = [B] }];

        GuaranteeRegistry.Create(definitions, failures);

        Assert.Equal(["The guarantee 'test.a' implies 'test.b', which is not defined."], failures);
    }

    [Fact]
    public void Create_WithTheBuiltInDefinitions_HasNoFailure()
    {
        var failures = new List<string>();

        var registry = GuaranteeRegistry.Create(Guarantees.Definitions, failures);
        var closed = registry.Close([Guarantees.LinearizableAcrossFailover]);

        Assert.Empty(failures);
        Assert.Equal(3, closed.Count);
        Assert.Contains(Guarantees.LinearizableAcrossFailover, closed);
        Assert.Contains(Guarantees.LinearizableUntilFailover, closed);
        Assert.Contains(Guarantees.LinearizableWithinProcess, closed);
    }

    #endregion

    #region Close Tests

    [Fact]
    public void Close_WhenImplicationsChain_IncludesEveryImpliedGuarantee()
    {
        var failures = new List<string>();
        GuaranteeDefinition[] definitions =
        [
            new(A, "test", "a") { Implies = [B] },
            new(B, "test", "b") { Implies = [C] },
            new(C, "test", "c"),
        ];
        var registry = GuaranteeRegistry.Create(definitions, failures);

        var closed = registry.Close([A]);

        Assert.Equal(3, closed.Count);
        Assert.Contains(A, closed);
        Assert.Contains(B, closed);
        Assert.Contains(C, closed);
    }

    [Fact]
    public void Close_WhenImplicationsCycle_Ends()
    {
        var failures = new List<string>();
        GuaranteeDefinition[] definitions =
        [
            new(A, "test", "a") { Implies = [B] },
            new(B, "test", "b") { Implies = [A] },
        ];
        var registry = GuaranteeRegistry.Create(definitions, failures);

        var closed = registry.Close([A]);

        Assert.Equal(2, closed.Count);
        Assert.Contains(A, closed);
        Assert.Contains(B, closed);
    }

    [Fact]
    public void Close_WhenAGuaranteeIsUndefined_KeepsItAlone()
    {
        var failures = new List<string>();
        GuaranteeDefinition[] definitions = [new(A, "test", "a")];

        var registry = GuaranteeRegistry.Create(definitions, failures);

        var closed = registry.Close([C]);

        Assert.Single(closed);
        Assert.Contains(C, closed);
    }

    #endregion
}
