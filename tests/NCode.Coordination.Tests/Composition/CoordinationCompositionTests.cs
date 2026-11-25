// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace NCode.Coordination.Composition.Tests;

public sealed class CoordinationCompositionTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    public void Dispose() => Mocks.Verify();

    private static ProviderDeclaration Declare(string name) =>
        new(name, PrimitiveSupport.Supported(Primitives.Occurrence, Guarantees.Durable));

    private static CoordinationRegistrations Register(
        ICoordinationProvider provider,
        params ProviderBinding[] bindings
    )
    {
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => provider);
        registrations.Bindings.AddRange(bindings);
        return registrations;
    }

    #region Resolve Tests

    [Fact]
    public void Resolve_WhenBound_ReturnsTheProvidersService()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var provider = new DisposableProvider(Declare("memory"), service);
        var composition = new CoordinationComposition(
            Register(provider, new ProviderBinding("memory")),
            Services
        );

        var bound = composition.Resolve<IOccurrenceService>(
            Primitives.Occurrence,
            new Keyspace("app", "tenant")
        );

        Assert.Equal("memory", bound.Provider);
        Assert.Equal(Primitives.Occurrence, bound.Primitive);
        Assert.Same(service, bound.Service);
    }

    [Fact]
    public void Resolve_WhenNoBindingCovers_ThrowsUnsupported()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var provider = new DisposableProvider(Declare("memory"), service);
        var composition = new CoordinationComposition(
            Register(provider, new ProviderBinding("memory") { Keyspace = new Keyspace("bound") }),
            Services
        );
        var keyspace = new Keyspace("other", "secret");

        var exception = Assert.Throws<CoordinationException>(() =>
            composition.Resolve<IOccurrenceService>(Primitives.Occurrence, keyspace)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(
            "No provider is bound to the primitive 'occurrence' for a keyspace of the cluster 'other'.",
            exception.Message
        );
    }

    #endregion

    #region EnsureComposed Tests

    [Fact]
    public void EnsureComposed_WhenInvalid_Throws()
    {
        var provider = new DisposableProvider(Declare("memory"));
        var registrations = Register(provider, new ProviderBinding("missing"));
        var composition = new CoordinationComposition(registrations, Services);

        Assert.Throws<CoordinationCompositionException>(composition.EnsureComposed);
    }

    [Fact]
    public void EnsureComposed_WhenValid_DoesNotThrow()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var provider = new DisposableProvider(Declare("memory"), service);
        var composition = new CoordinationComposition(
            Register(provider, new ProviderBinding("memory")),
            Services
        );

        composition.EnsureComposed();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_DisposesEachProvider()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var sync = new DisposableProvider(Declare("sync"), service);
        var async = new AsyncDisposableProvider(Declare("async"));
        var registrations = Register(sync, new ProviderBinding("sync"));
        registrations.ProviderFactories.Add(_ => async);
        var composition = new CoordinationComposition(registrations, Services);
        composition.EnsureComposed();

        await composition.DisposeAsync();

        Assert.Equal(1, sync.Disposals);
        Assert.Equal(1, async.Disposals);
    }

    [Fact]
    public void Dispose_DisposesTheDisposableProviders()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var sync = new DisposableProvider(Declare("sync"), service);
        var async = new AsyncDisposableProvider(Declare("async"));
        var registrations = Register(sync, new ProviderBinding("sync"));
        registrations.ProviderFactories.Add(_ => async);
        var composition = new CoordinationComposition(registrations, Services);
        composition.EnsureComposed();

        composition.Dispose();

        Assert.Equal(1, sync.Disposals);
        Assert.Equal(0, async.Disposals);
    }

    [Fact]
    public async Task DisposeAsync_WhenNeverComposed_CreatesNoProvider()
    {
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ =>
            throw new InvalidOperationException("The factory must not run.")
        );
        var composition = new CoordinationComposition(registrations, Services);

        await composition.DisposeAsync();
    }

    #endregion

    private sealed class DisposableProvider(ProviderDeclaration declaration, object? service = null)
        : ICoordinationProvider,
            IDisposable
    {
        public int Disposals { get; private set; }

        public ProviderDeclaration Declaration { get; } = declaration;

        public object? GetPrimitiveService(Type serviceType) =>
            service is not null && serviceType.IsInstanceOfType(service) ? service : null;

        public void Dispose() => Disposals++;
    }

    private sealed class AsyncDisposableProvider(ProviderDeclaration declaration)
        : ICoordinationProvider,
            IAsyncDisposable
    {
        public int Disposals { get; private set; }

        public ProviderDeclaration Declaration { get; } = declaration;

        public object? GetPrimitiveService(Type serviceType) => null;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
