// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class CoordinationCompositionTests : IDisposable
{
    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    private static ProviderDeclaration Declare(string name)
    {
        var support = PrimitiveSupport.Supported(Primitives.Occurrence, Guarantees.Durable);
        return new ProviderDeclaration(name, support);
    }

    private static CoordinationRegistrations Register(
        ICoordinationProvider provider,
        params IEnumerable<ProviderBinding> bindings
    )
    {
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => provider);
        registrations.Bindings.AddRange(bindings);
        return registrations;
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region Resolve Tests

    [Fact]
    public void Resolve_WhenBound_ReturnsTheProvidersService()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var declaration = Declare("memory");
        var provider = new DisposableProvider(declaration, service);
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);
        var keyspace = new Keyspace("app", "tenant");

        var bound = composition.Resolve<IOccurrenceService>(Primitives.Occurrence, keyspace);

        Assert.Equal("memory", bound.Provider);
        Assert.Equal(Primitives.Occurrence, bound.Primitive);
        Assert.Same(service, bound.Service);
    }

    [Fact]
    public void Resolve_WhenNoBindingCovers_ThrowsUnsupported()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var declaration = Declare("memory");
        var provider = new DisposableProvider(declaration, service);
        var binding = new ProviderBinding("memory") { Keyspace = new Keyspace("bound") };
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);
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
        var declaration = Declare("memory");
        var provider = new DisposableProvider(declaration);
        var binding = new ProviderBinding("missing");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);

        Assert.Throws<CoordinationCompositionException>(composition.EnsureComposed);
    }

    [Fact]
    public void EnsureComposed_WhenValid_DoesNotThrow()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var declaration = Declare("memory");
        var provider = new DisposableProvider(declaration, service);
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);

        composition.EnsureComposed();
    }

    [Fact]
    public void EnsureComposed_WhenAFactoryThrows_RethrowsItsException()
    {
        var failure = new InvalidOperationException("The factory failed.");
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => throw failure);
        var composition = new CoordinationComposition(registrations, Services);

        var thrown = Assert.Throws<InvalidOperationException>(composition.EnsureComposed);

        Assert.Same(failure, thrown);
    }

    #endregion

    #region EnsureStartedAsync Tests

    [Fact]
    public async Task EnsureStartedAsync_ProbesEveryProviderInRegistrationOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probed = new List<string>();
        var tokens = new List<CancellationToken>();
        var firstService = Mocks.Create<IOccurrenceService>().Object;
        var secondService = Mocks.Create<IOccurrenceService>().Object;
        var firstDeclaration = Declare("first");
        var secondDeclaration = Declare("second");
        var first = new TestProvider(firstDeclaration, firstService)
        {
            Probe = token =>
            {
                probed.Add("first");
                tokens.Add(token);
                return ValueTask.CompletedTask;
            },
        };
        var second = new TestProvider(secondDeclaration, secondService)
        {
            Probe = token =>
            {
                probed.Add("second");
                tokens.Add(token);
                return ValueTask.CompletedTask;
            },
        };
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => first);
        registrations.ProviderFactories.Add(_ => second);
        var firstKeyspace = new Keyspace("a");
        var secondKeyspace = new Keyspace("b");
        var firstBinding = new ProviderBinding("first") { Keyspace = firstKeyspace };
        var secondBinding = new ProviderBinding("second") { Keyspace = secondKeyspace };
        registrations.Bindings.Add(firstBinding);
        registrations.Bindings.Add(secondBinding);
        var composition = new CoordinationComposition(registrations, Services);

        await composition.EnsureStartedAsync(cancellationToken);

        Assert.Equal(["first", "second"], probed);
        Assert.Equal([cancellationToken, cancellationToken], tokens);
    }

    [Fact]
    public async Task EnsureStartedAsync_WhenAProbeThrows_ThrowsItsException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new CoordinationException(
            CoordinationErrorCodes.Unsupported,
            outcomeUnknown: false,
            "probe"
        );
        var service = Mocks.Create<IOccurrenceService>().Object;
        var declaration = Declare("memory");
        var provider = new TestProvider(declaration, service)
        {
            Probe = _ => ValueTask.FromException(failure),
        };
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);

        var thrown = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task EnsureStartedAsync_WhenTheCompositionIsInvalid_ThrowsWithoutProbing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probed = false;
        var declaration = Declare("memory");
        var provider = new TestProvider(declaration)
        {
            Probe = _ =>
            {
                probed = true;
                return ValueTask.CompletedTask;
            },
        };
        var binding = new ProviderBinding("missing");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);

        await Assert.ThrowsAsync<CoordinationCompositionException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        Assert.False(probed);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_DisposesEachProvider()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var syncDeclaration = Declare("sync");
        var asyncDeclaration = Declare("async");
        var sync = new DisposableProvider(syncDeclaration, service);
        var async = new AsyncDisposableProvider(asyncDeclaration);
        var binding = new ProviderBinding("sync");
        var registrations = Register(sync, binding);
        registrations.ProviderFactories.Add(_ => async);
        var composition = new CoordinationComposition(registrations, Services);
        composition.EnsureComposed();

        await composition.DisposeAsync();

        Assert.Equal(1, sync.Disposals);
        Assert.Equal(1, async.Disposals);
    }

    [Fact]
    public async Task DisposeAsync_WhenTheCompositionIsInvalid_DisposesTheCreatedProviders()
    {
        var declaration = Declare("memory");
        var provider = new AsyncDisposableProvider(declaration);
        var binding = new ProviderBinding("missing");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);
        Assert.Throws<CoordinationCompositionException>(composition.EnsureComposed);

        await composition.DisposeAsync();

        Assert.Equal(1, provider.Disposals);
    }

    [Fact]
    public void Dispose_DisposesTheDisposableProviders()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var syncDeclaration = Declare("sync");
        var asyncDeclaration = Declare("async");
        var sync = new DisposableProvider(syncDeclaration, service);
        var async = new AsyncDisposableProvider(asyncDeclaration);
        var binding = new ProviderBinding("sync");
        var registrations = Register(sync, binding);
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

        /// <inheritdoc />
        public ValueTask ProbeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose() => Disposals++;
    }

    private sealed class AsyncDisposableProvider(ProviderDeclaration declaration)
        : ICoordinationProvider,
            IAsyncDisposable
    {
        public int Disposals { get; private set; }

        public ProviderDeclaration Declaration { get; } = declaration;

        public object? GetPrimitiveService(Type serviceType) => null;

        /// <inheritdoc />
        public ValueTask ProbeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
