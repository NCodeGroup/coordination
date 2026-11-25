// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NCode.Coordination.Barriers;
using NCode.Coordination.Caching;
using NCode.Coordination.Composition;
using NCode.Coordination.Locks;
using NCode.Coordination.Partitions;
using NCode.Coordination.RateLimiting;
using NCode.Coordination.SingleFlight;
using NCode.Coordination.Singletons;

namespace NCode.Coordination.Tests;

public sealed class CoordinationServiceCollectionExtensionsTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private TestProvider CreateProvider(params IEnumerable<Guarantee> guarantees)
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var support = PrimitiveSupport.Supported(Primitives.Occurrence, guarantees);
        var declaration = new ProviderDeclaration(TestComposition.ProviderName, support);
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
    public async Task AddCoordination_WhenCalled_ResolvesTheComposition()
    {
        var provider = CreateProvider();
        var binding = new ProviderBinding(TestComposition.ProviderName);
        var services = new ServiceCollection();
        services.AddCoordination(b => b.AddProvider(_ => provider).Bind(binding));
        await using var serviceProvider = services.BuildServiceProvider();

        var composition = serviceProvider.GetRequiredService<CoordinationComposition>();

        var bound = composition.Resolve<IOccurrenceService>(
            Primitives.Occurrence,
            TestComposition.Keyspace
        );
        Assert.Equal(TestComposition.ProviderName, bound.Provider);
    }

    [Fact]
    public async Task AddCoordination_WhenCalledTwice_AddsToOneComposition()
    {
        var provider = CreateProvider(Guarantees.Durable);
        var binding = new ProviderBinding(TestComposition.ProviderName);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.Durable
        );
        var services = new ServiceCollection();
        services.AddCoordination(b => b.AddProvider(_ => provider).Bind(binding));
        services.AddCoordination(b => b.Require(requirement));
        await using var serviceProvider = services.BuildServiceProvider();

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
    public async Task AddCoordination_WhenCalled_RegistersTheSystemClock()
    {
        var services = new ServiceCollection();
        services.AddCoordination(_ => { });
        await using var serviceProvider = services.BuildServiceProvider();

        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

        Assert.Same(TimeProvider.System, timeProvider);
    }

    [Fact]
    public void AddCoordination_WhenCalled_RegistersARoutedServiceForEveryBuiltInPrimitive()
    {
        var services = new ServiceCollection();

        services.AddCoordination(_ => { });

        foreach (var primitive in Primitives.All)
        {
            var serviceType = PrimitiveServiceTypes.Find(primitive);
            Assert.NotNull(serviceType);
            var descriptor = Assert.Single(services, x => x.ServiceType == serviceType);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.Equal("NCode.Coordination.Routing", descriptor.ImplementationType?.Namespace);
        }
    }

    [Fact]
    public async Task AddCoordination_WhenCalled_RegistersEveryRecipe()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddCoordination(_ => { });
        await using var serviceProvider = services.BuildServiceProvider();

        var lockService = serviceProvider.GetRequiredService<ILockService>();
        var readWriteLockService = serviceProvider.GetRequiredService<IReadWriteLockService>();
        var barrierService = serviceProvider.GetRequiredService<IBarrierService>();
        var singletonService = serviceProvider.GetRequiredService<ISingletonService>();
        var partitionOwnershipService =
            serviceProvider.GetRequiredService<IPartitionOwnershipService>();
        var singleFlightService = serviceProvider.GetRequiredService<ISingleFlightService>();
        var rateLimiterFactory = serviceProvider.GetRequiredService<IRateLimiterFactory>();
        var cacheInvalidationService =
            serviceProvider.GetRequiredService<ICacheInvalidationService>();

        Assert.IsType<DefaultLockService>(lockService);
        Assert.IsType<DefaultReadWriteLockService>(readWriteLockService);
        Assert.IsType<DefaultBarrierService>(barrierService);
        Assert.IsType<DefaultSingletonService>(singletonService);
        Assert.IsType<DefaultPartitionOwnershipService>(partitionOwnershipService);
        Assert.IsType<DefaultSingleFlightService>(singleFlightService);
        Assert.IsType<DefaultRateLimiterFactory>(rateLimiterFactory);
        Assert.IsType<DefaultCacheInvalidationService>(cacheInvalidationService);
    }

    [Fact]
    public async Task AddCoordination_WhenTheHostStartsWithAnUnmetRequirement_FailsListingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        var requirement = new FeatureRequirement("replay", Primitives.Occurrence);
        builder.Services.AddCoordination(b => b.Require(requirement));
        using IHost host = builder.Build();

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
    public async Task AddCoordination_WhenTheHostStartsWithAValidComposition_Starts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = CreateProvider();
        var binding = new ProviderBinding(TestComposition.ProviderName);
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Services.AddCoordination(b => b.AddProvider(_ => provider).Bind(binding));
        using IHost host = builder.Build();

        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    #endregion
}
