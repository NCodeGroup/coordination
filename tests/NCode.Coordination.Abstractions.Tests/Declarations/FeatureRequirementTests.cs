// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class FeatureRequirementTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValues()
    {
        var primitive = new Primitive("lease");
        var durable = new Guarantee("durable");
        var replicated = new Guarantee("replicated");

        var requirement = new FeatureRequirement("leader-election", primitive, durable, replicated);

        Assert.Equal("leader-election", requirement.Feature);
        Assert.Same(primitive, requirement.Primitive);
        Assert.Equal<Guarantee>([durable, replicated], requirement.Guarantees);
    }

    [Fact]
    public void Constructor_WhenNoGuaranteesAreGiven_HasNoGuarantees()
    {
        var requirement = new FeatureRequirement("leader-election", new Primitive("lease"));

        Assert.False(requirement.Guarantees.IsDefault);
        Assert.Empty(requirement.Guarantees);
    }

    [Fact]
    public void Constructor_WhenFeatureIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new FeatureRequirement("Bad Feature", new Primitive("lease"))
        );

        Assert.Equal("feature", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenPrimitiveIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new FeatureRequirement("leader-election", null!)
        );

        Assert.Equal("primitive", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAGuaranteeIsNull_Throws()
    {
        ImmutableArray<Guarantee> guarantees = [new Guarantee("durable"), null!];

        var exception = Assert.Throws<ArgumentException>(() =>
            new FeatureRequirement("leader-election", new Primitive("lease"), guarantees)
        );

        Assert.Equal("guarantees", exception.ParamName);
    }

    #endregion

    #region Keyspace Property Tests

    [Fact]
    public void Keyspace_ByDefault_IsNull()
    {
        var requirement = new FeatureRequirement("leader-election", new Primitive("lease"));

        Assert.Null(requirement.Keyspace);
    }

    [Fact]
    public void Keyspace_WhenSet_KeepsTheKeyspace()
    {
        var keyspace = new Keyspace("prod", "cell-1");

        var requirement = new FeatureRequirement("leader-election", new Primitive("lease"))
        {
            Keyspace = keyspace,
        };

        Assert.Equal(keyspace, requirement.Keyspace);
    }

    #endregion
}
