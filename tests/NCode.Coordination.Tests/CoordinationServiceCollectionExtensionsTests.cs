// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NCode.Coordination.Composition;

namespace NCode.Coordination.Tests;

public sealed class CoordinationServiceCollectionExtensionsTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private TestProvider CreateProvider(params Guarantee[] guarantees)
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Occurrence, guarantees)
        );
        return new TestProvider(declaration, service);
    }

    #region AddCoordination Tests

    [Fact]
    public void AddCoordination_WhenConfigureIsNull_Throws()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentNullException>(() => services.AddCoordination(null!));

        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public void AddCoordination_ResolvesTheComposition()
    {
        var provider = CreateProvider();
        var services = new ServiceCollection();
        services.AddCoordination(b =>
            b.AddProvider(_ => provider).Bind(new ProviderBinding(TestComposition.ProviderName))
        );
        using var serviceProvider = services.BuildServiceProvider();

        var composition = serviceProvider.GetRequiredService<CoordinationComposition>();

        var bound = composition.Resolve<IOccurrenceService>(
            Primitives.Occurrence,
            TestComposition.Keyspace
        );
        Assert.Equal(TestComposition.ProviderName, bound.Provider);
    }

    [Fact]
    public void AddCoordination_WhenCalledTwice_AddsToOneComposition()
    {
        var provider = CreateProvider(Guarantees.Durable);
        var services = new ServiceCollection();
        services.AddCoordination(b =>
            b.AddProvider(_ => provider).Bind(new ProviderBinding(TestComposition.ProviderName))
        );
        services.AddCoordination(b =>
            b.Require(new FeatureRequirement("replay", Primitives.Occurrence, Guarantees.Durable))
        );
        using var serviceProvider = services.BuildServiceProvider();

        var composition = serviceProvider.GetRequiredService<CoordinationComposition>();

        var bound = composition.Resolve<IOccurrenceService>(
            Primitives.Occurrence,
            TestComposition.Keyspace
        );
        Assert.Equal(TestComposition.ProviderName, bound.Provider);
    }

    [Fact]
    public void AddCoordination_WhenCalledTwice_RegistersOneStartupCheck()
    {
        var services = new ServiceCollection();

        services.AddCoordination(_ => { });
        services.AddCoordination(_ => { });

        var checks = services.Count(x => x.ImplementationType == typeof(CoordinationStartupCheck));
        Assert.Equal(1, checks);
    }

    [Fact]
    public void AddCoordination_RegistersTheSystemClock()
    {
        var services = new ServiceCollection();
        services.AddCoordination(_ => { });
        using var serviceProvider = services.BuildServiceProvider();

        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

        Assert.Same(TimeProvider.System, timeProvider);
    }

    #endregion

    #region Host Tests

    [Fact]
    public async Task HostStart_WhenARequirementIsUnmet_FailsListingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddCoordination(b =>
            b.Require(new FeatureRequirement("replay", Primitives.Occurrence))
        );
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<CoordinationCompositionException>(() =>
            host.StartAsync(cancellationToken)
        );

        var failure = Assert.Single(exception.Failures);
        Assert.Equal(
            "The feature 'replay' requires the primitive 'occurrence' by default, and no provider is bound to it.",
            failure
        );
    }

    [Fact]
    public async Task HostStart_WhenValid_Starts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = CreateProvider();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddCoordination(b =>
            b.AddProvider(_ => provider).Bind(new ProviderBinding(TestComposition.ProviderName))
        );
        using var host = builder.Build();

        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    #endregion
}
