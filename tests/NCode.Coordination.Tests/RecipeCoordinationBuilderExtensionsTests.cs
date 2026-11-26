// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCode.Coordination.Composition;

namespace NCode.Coordination.Tests;

public sealed class RecipeCoordinationBuilderExtensionsTests
{
    private static ProviderBinding Binding { get; } =
        new(InProcessCoordinationBuilderExtensions.ProviderName);

    #region RequireRecipe Tests

    [Fact]
    public void RequireRecipe_WhenGivenAFeature_AddsOneRequirementPerNeed()
    {
        var registrations = new CoordinationRegistrations();
        var builder = new DefaultCoordinationBuilder(registrations);

        var result = builder.RequireRecipe(CoordinationRecipes.ReadWriteLock, "orders");

        Assert.Same(builder, result);
        Assert.Collection(
            registrations.Requirements,
            requirement =>
            {
                Assert.Equal("orders", requirement.Feature);
                Assert.Equal(Primitives.Lease, requirement.Primitive);
                Assert.Equal<Guarantee>(
                    [Guarantees.LinearizableUntilFailover],
                    requirement.Guarantees
                );
                Assert.Null(requirement.Keyspace);
            },
            requirement =>
            {
                Assert.Equal("orders", requirement.Feature);
                Assert.Equal(Primitives.Record, requirement.Primitive);
                Assert.Equal<Guarantee>(
                    [Guarantees.LinearizableUntilFailover],
                    requirement.Guarantees
                );
                Assert.Null(requirement.Keyspace);
            },
            requirement =>
            {
                Assert.Equal("orders", requirement.Feature);
                Assert.Equal(Primitives.RecordWatch, requirement.Primitive);
                Assert.Equal<Guarantee>(
                    [Guarantees.AtMostOnceWithGapDetection],
                    requirement.Guarantees
                );
                Assert.Null(requirement.Keyspace);
            }
        );
    }

    [Fact]
    public void RequireRecipe_WhenGivenAKeyspace_AddsEveryRequirementOnIt()
    {
        var registrations = new CoordinationRegistrations();
        var builder = new DefaultCoordinationBuilder(registrations);
        var keyspace = new Keyspace("orders-space");

        builder.RequireRecipe(CoordinationRecipes.ReadWriteLock, "orders", keyspace);

        Assert.Equal(3, registrations.Requirements.Count);
        Assert.All(registrations.Requirements, requirement =>
            Assert.Equal(keyspace, requirement.Keyspace)
        );
    }

    [Fact]
    public void RequireRecipe_WhenTheFeatureIsNotAName_ThrowsAndAddsNothing()
    {
        var registrations = new CoordinationRegistrations();
        var builder = new DefaultCoordinationBuilder(registrations);

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.RequireRecipe(CoordinationRecipes.ReadWriteLock, "Not A Name")
        );

        Assert.Equal("feature", exception.ParamName);
        Assert.Empty(registrations.Requirements);
    }

    [Fact]
    public void RequireRecipe_WhenTheRecipeIsNull_ThrowsNamingTheRecipe()
    {
        var registrations = new CoordinationRegistrations();
        var builder = new DefaultCoordinationBuilder(registrations);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            builder.RequireRecipe(null!, "orders")
        );

        Assert.Equal("recipe", exception.ParamName);
    }

    [Fact]
    public void RequireRecipe_WhenTheKeyspaceIsNull_ThrowsNamingTheKeyspace()
    {
        var registrations = new CoordinationRegistrations();
        var builder = new DefaultCoordinationBuilder(registrations);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            builder.RequireRecipe(CoordinationRecipes.Lock, "orders", null!)
        );

        Assert.Equal("keyspace", exception.ParamName);
    }

    [Fact]
    public void RequireRecipe_WhenTheBuilderIsNull_ThrowsNamingTheBuilder()
    {
        ICoordinationBuilder builder = null!;

        var exception = Assert.Throws<ArgumentNullException>(() =>
            builder.RequireRecipe(CoordinationRecipes.Lock, "orders")
        );

        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public async Task RequireRecipe_WhenTheSingleNodeProviderKeepsEveryRecipe_StartsTheHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Services.AddMetrics();
        builder.Services.AddCoordination(b =>
        {
            b.AddInProcessProvider().Bind(Binding).UseSingleNode();
            for (var index = 0; index < CoordinationRecipes.All.Length; index++)
            {
                b.RequireRecipe(CoordinationRecipes.All[index], $"feature-{index}");
            }
        });
        using IHost host = builder.Build();

        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    [Fact]
    public async Task RequireRecipe_WhenTheProviderDoesNotKeepAGuarantee_FailsTheStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Services.AddMetrics();
        builder.Services.AddCoordination(b =>
            b.AddInProcessProvider()
                .Bind(Binding)
                .RequireRecipe(CoordinationRecipes.Lock, "checkout")
        );
        using IHost host = builder.Build();

        var exception = await Assert.ThrowsAsync<CoordinationCompositionException>(() =>
            host.StartAsync(cancellationToken)
        );

        var failure = Assert.Single(exception.Failures);
        Assert.Equal(
            "The feature 'checkout' requires 'consistency.linearizable-until-failover' on the primitive 'lease' by default, which the provider 'in-process' does not keep.",
            failure
        );
    }

    #endregion
}
