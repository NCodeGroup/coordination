// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class DefaultCoordinationBuilderTests
{
    private CoordinationRegistrations Registrations { get; } = new();

    private DefaultCoordinationBuilder CreateBuilder() => new(Registrations);

    #region AddProvider Tests

    [Fact]
    public void AddProvider_AppendsTheFactoryAndReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        Func<IServiceProvider, ICoordinationProvider> factory = _ => new TestProvider(
            new ProviderDeclaration("memory")
        );

        var result = builder.AddProvider(factory);

        Assert.Same(builder, result);
        Assert.Equal([factory], Registrations.ProviderFactories);
    }

    [Fact]
    public void AddProvider_WhenFactoryIsNull_Throws()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.AddProvider(null!));
    }

    #endregion

    #region Bind Tests

    [Fact]
    public void Bind_AppendsTheBindingAndReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        var binding = new ProviderBinding("memory");

        var result = builder.Bind(binding);

        Assert.Same(builder, result);
        Assert.Equal([binding], Registrations.Bindings);
    }

    [Fact]
    public void Bind_WhenBindingIsNull_Throws()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.Bind(null!));
    }

    #endregion

    #region Require Tests

    [Fact]
    public void Require_AppendsTheRequirementAndReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        var requirement = new FeatureRequirement("replay", new Primitive("occurrence"));

        var result = builder.Require(requirement);

        Assert.Same(builder, result);
        Assert.Equal([requirement], Registrations.Requirements);
    }

    [Fact]
    public void Require_WhenRequirementIsNull_Throws()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.Require(null!));
    }

    #endregion

    #region Define Tests

    [Fact]
    public void Define_AppendsTheDefinitionAndReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        var definition = new GuaranteeDefinition(
            new Guarantee("custom.strong"),
            "custom",
            "A custom guarantee."
        );

        var result = builder.Define(definition);

        Assert.Same(builder, result);
        Assert.Equal([definition], Registrations.Definitions);
    }

    [Fact]
    public void Define_WhenDefinitionIsNull_Throws()
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.Define(null!));
    }

    #endregion

    #region UseSingleNode Tests

    [Fact]
    public void UseSingleNode_SetsSingleNode()
    {
        var builder = CreateBuilder();

        var result = builder.UseSingleNode();

        Assert.Same(builder, result);
        Assert.True(Registrations.IsSingleNode);
    }

    #endregion
}
