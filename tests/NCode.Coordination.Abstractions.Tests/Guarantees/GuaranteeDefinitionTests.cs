// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class GuaranteeDefinitionTests
{
    private const string Description = "Data written survives a restart.";

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValues()
    {
        var guarantee = new Guarantee("durable");

        var definition = new GuaranteeDefinition(
            guarantee,
            GuaranteeDimensions.Durability,
            Description
        );

        Assert.Same(guarantee, definition.Guarantee);
        Assert.Equal(GuaranteeDimensions.Durability, definition.Dimension);
        Assert.Equal(Description, definition.Description);
    }

    [Fact]
    public void Constructor_WhenGuaranteeIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new GuaranteeDefinition(null!, GuaranteeDimensions.Durability, Description)
        );

        Assert.Equal("guarantee", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenDimensionIsInvalid_Throws()
    {
        var guarantee = new Guarantee("durable");

        var exception = Assert.Throws<ArgumentException>(() =>
            new GuaranteeDefinition(guarantee, "Bad Dimension", Description)
        );

        Assert.Equal("dimension", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhenDescriptionIsBlank_Throws(string description)
    {
        var guarantee = new Guarantee("durable");

        var exception = Assert.Throws<ArgumentException>(() =>
            new GuaranteeDefinition(guarantee, GuaranteeDimensions.Durability, description)
        );

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenDescriptionIsNull_Throws()
    {
        var guarantee = new Guarantee("durable");

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new GuaranteeDefinition(guarantee, GuaranteeDimensions.Durability, null!)
        );

        Assert.Equal("description", exception.ParamName);
    }

    #endregion

    #region Implies Property Tests

    [Fact]
    public void Implies_ByDefault_IsEmpty()
    {
        var definition = new GuaranteeDefinition(
            new Guarantee("durable"),
            GuaranteeDimensions.Durability,
            Description
        );

        Assert.False(definition.Implies.IsDefault);
        Assert.Empty(definition.Implies);
    }

    [Fact]
    public void Implies_WhenSet_KeepsTheGuarantees()
    {
        var volatileGuarantee = new Guarantee("volatile");

        var definition = new GuaranteeDefinition(
            new Guarantee("durable"),
            GuaranteeDimensions.Durability,
            Description
        )
        {
            Implies = [volatileGuarantee],
        };

        Assert.Equal<Guarantee>([volatileGuarantee], definition.Implies);
    }

    [Fact]
    public void Implies_WhenSetToDefaultArray_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new GuaranteeDefinition(
                new Guarantee("durable"),
                GuaranteeDimensions.Durability,
                Description
            )
            {
                Implies = default,
            }
        );

        Assert.Equal("Implies", exception.ParamName);
    }

    [Fact]
    public void Implies_WhenAnElementIsNull_Throws()
    {
        ImmutableArray<Guarantee> implies = [new Guarantee("volatile"), null!];

        var exception = Assert.Throws<ArgumentException>(() =>
            new GuaranteeDefinition(
                new Guarantee("durable"),
                GuaranteeDimensions.Durability,
                Description
            )
            {
                Implies = implies,
            }
        );

        Assert.Equal("Implies", exception.ParamName);
    }

    #endregion
}
