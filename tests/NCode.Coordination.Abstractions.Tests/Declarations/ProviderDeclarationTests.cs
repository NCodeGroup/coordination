// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class ProviderDeclarationTests
{
    private const string Description = "Data written survives a restart.";

    private static GuaranteeDefinition CreateDefinition(string guarantee) =>
        new(new Guarantee(guarantee), GuaranteeDimensions.Durability, Description);

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValues()
    {
        var lease = PrimitiveSupport.Supported(
            NCode.Coordination.Primitives.Lease,
            new Guarantee("durable")
        );
        var counter = PrimitiveSupport.Unsupported(
            new Primitive("counter"),
            "The store has no counters."
        );

        var declaration = new ProviderDeclaration("memory", lease, counter);

        Assert.Equal("memory", declaration.Provider);
        Assert.Equal<PrimitiveSupport>([lease, counter], declaration.Primitives);
    }

    [Fact]
    public void Constructor_WhenNoPrimitivesAreGiven_HasNoPrimitives()
    {
        var declaration = new ProviderDeclaration("memory");

        Assert.False(declaration.Primitives.IsDefault);
        Assert.Empty(declaration.Primitives);
    }

    [Fact]
    public void Constructor_WhenProviderIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderDeclaration("Bad Provider")
        );

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenProviderIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ProviderDeclaration(null!));

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenASupportIsNull_Throws()
    {
        ImmutableArray<PrimitiveSupport> primitives =
        [
            PrimitiveSupport.Supported(new Primitive("lease")),
            null!,
        ];

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderDeclaration("memory", primitives)
        );

        Assert.Equal("primitives", exception.ParamName);
    }

    #endregion

    #region Profile Property Tests

    [Fact]
    public void Profile_ByDefault_IsNull()
    {
        var declaration = new ProviderDeclaration("memory");

        Assert.Null(declaration.Profile);
    }

    [Fact]
    public void Profile_WhenSet_KeepsTheProfile()
    {
        var declaration = new ProviderDeclaration("memory") { Profile = "garnet" };

        Assert.Equal("garnet", declaration.Profile);
    }

    [Fact]
    public void Profile_WhenSetToNull_StaysNull()
    {
        var declaration = new ProviderDeclaration("memory") { Profile = null };

        Assert.Null(declaration.Profile);
    }

    [Fact]
    public void Profile_WhenInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderDeclaration("memory") { Profile = "Bad Profile" }
        );

        Assert.Equal("Profile", exception.ParamName);
    }

    #endregion

    #region Definitions Property Tests

    [Fact]
    public void Definitions_ByDefault_IsEmpty()
    {
        var declaration = new ProviderDeclaration("memory");

        Assert.False(declaration.Definitions.IsDefault);
        Assert.Empty(declaration.Definitions);
    }

    [Fact]
    public void Definitions_WhenSet_KeepsTheDefinitions()
    {
        var definition = CreateDefinition("durable");

        var declaration = new ProviderDeclaration("memory") { Definitions = [definition] };

        Assert.Equal<GuaranteeDefinition>([definition], declaration.Definitions);
    }

    [Fact]
    public void Definitions_WhenSetToDefaultArray_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderDeclaration("memory") { Definitions = default }
        );

        Assert.Equal("Definitions", exception.ParamName);
    }

    [Fact]
    public void Definitions_WhenAnElementIsNull_Throws()
    {
        ImmutableArray<GuaranteeDefinition> definitions = [CreateDefinition("durable"), null!];

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderDeclaration("memory") { Definitions = definitions }
        );

        Assert.Equal("Definitions", exception.ParamName);
    }

    #endregion
}
