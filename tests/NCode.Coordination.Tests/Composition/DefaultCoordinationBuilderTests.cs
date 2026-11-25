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
    public void AddProvider_WhenCalled_RecordsTheFactory()
    {
        var builder = CreateBuilder();
        var declaration = new ProviderDeclaration("memory");
        Func<IServiceProvider, ICoordinationProvider> factory = _ => new TestProvider(declaration);

        builder.AddProvider(factory);

        Assert.Equal<Func<IServiceProvider, ICoordinationProvider>>(
            [factory],
            Registrations.ProviderFactories
        );
    }

    [Fact]
    public void AddProvider_WhenCalled_ReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        var declaration = new ProviderDeclaration("memory");
        Func<IServiceProvider, ICoordinationProvider> factory = _ => new TestProvider(declaration);

        var result = builder.AddProvider(factory);

        Assert.Same(builder, result);
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
    public void Bind_WhenCalled_RecordsTheBinding()
    {
        var builder = CreateBuilder();
        var binding = new ProviderBinding("memory");

        builder.Bind(binding);

        Assert.Equal<ProviderBinding>([binding], Registrations.Bindings);
    }

    [Fact]
    public void Bind_WhenCalled_ReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        var binding = new ProviderBinding("memory");

        var result = builder.Bind(binding);

        Assert.Same(builder, result);
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
    public void Require_WhenCalled_RecordsTheRequirement()
    {
        var builder = CreateBuilder();
        var primitive = new Primitive("occurrence");
        var requirement = new FeatureRequirement("replay", primitive);

        builder.Require(requirement);

        Assert.Equal<FeatureRequirement>([requirement], Registrations.Requirements);
    }

    [Fact]
    public void Require_WhenCalled_ReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        var primitive = new Primitive("occurrence");
        var requirement = new FeatureRequirement("replay", primitive);

        var result = builder.Require(requirement);

        Assert.Same(builder, result);
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
    public void Define_WhenCalled_RecordsTheDefinition()
    {
        var builder = CreateBuilder();
        var guarantee = new Guarantee("custom.strong");
        var definition = new GuaranteeDefinition(guarantee, "custom", "A custom guarantee.");

        builder.Define(definition);

        Assert.Equal<GuaranteeDefinition>([definition], Registrations.Definitions);
    }

    [Fact]
    public void Define_WhenCalled_ReturnsTheBuilder()
    {
        var builder = CreateBuilder();
        var guarantee = new Guarantee("custom.strong");
        var definition = new GuaranteeDefinition(guarantee, "custom", "A custom guarantee.");

        var result = builder.Define(definition);

        Assert.Same(builder, result);
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
    public void UseSingleNode_WhenCalled_SetsSingleNode()
    {
        var builder = CreateBuilder();

        builder.UseSingleNode();

        Assert.True(Registrations.IsSingleNode);
    }

    [Fact]
    public void UseSingleNode_WhenCalled_ReturnsTheBuilder()
    {
        var builder = CreateBuilder();

        var result = builder.UseSingleNode();

        Assert.Same(builder, result);
    }

    #endregion
}
