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

    #region EnsureStartedAsync Tests

    [Fact]
    public async Task EnsureStartedAsync_WhenAFactoryThrows_RethrowsItsException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("The factory failed.");
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => throw failure);
        var composition = new CoordinationComposition(registrations, Services);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task EnsureStartedAsync_WhenValid_ProbesEveryProviderInRegistrationOrder()
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

        Assert.Equal<string>(["first", "second"], probed);
        Assert.Equal<CancellationToken>([cancellationToken, cancellationToken], tokens);
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
    public async Task EnsureStartedAsync_WhenCanceled_ThrowsOperationCanceled()
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var declaration = Declare("memory");
        var provider = new TestProvider(declaration, service)
        {
            Probe = token =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            },
        };
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var cancellationToken = source.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );
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

        var thrown = await Assert.ThrowsAsync<CoordinationCompositionException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        Assert.Equal<string>(
            ["A binding names the provider 'missing', which is not registered."],
            thrown.Failures
        );
        Assert.False(probed);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenComposed_DisposesEachProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = Mocks.Create<IOccurrenceService>().Object;
        var syncDeclaration = Declare("sync");
        var asyncDeclaration = Declare("async");
        var sync = new DisposableProvider(syncDeclaration, service);
        var async = new AsyncDisposableProvider(asyncDeclaration);
        var binding = new ProviderBinding("sync");
        var registrations = Register(sync, binding);
        registrations.ProviderFactories.Add(_ => async);
        var composition = new CoordinationComposition(registrations, Services);
        await composition.EnsureStartedAsync(cancellationToken);

        await composition.DisposeAsync();

        Assert.Equal(1, sync.Disposals);
        Assert.Equal(1, async.Disposals);
    }

    [Fact]
    public async Task DisposeAsync_WhenTheCompositionIsInvalid_DisposesTheCreatedProviders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var declaration = Declare("memory");
        var provider = new AsyncDisposableProvider(declaration);
        var binding = new ProviderBinding("missing");
        var registrations = Register(provider, binding);
        var composition = new CoordinationComposition(registrations, Services);
        await Assert.ThrowsAsync<CoordinationCompositionException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        await composition.DisposeAsync();

        Assert.Equal(1, provider.Disposals);
    }

    [Fact]
    public async Task DisposeAsync_WhenAProviderIsRejected_DisposesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var declaration = Declare("memory");
        var first = new AsyncDisposableProvider(declaration);
        var second = new AsyncDisposableProvider(declaration);
        var binding = new ProviderBinding("memory");
        var registrations = Register(first, binding);
        registrations.ProviderFactories.Add(_ => second);
        var composition = new CoordinationComposition(registrations, Services);
        await Assert.ThrowsAsync<CoordinationCompositionException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        await composition.DisposeAsync();

        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
    }

    [Fact]
    public async Task DisposeAsync_WhenAProviderDisposalThrows_DisposesTheRestAndRethrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstException = new InvalidOperationException("first");
        var firstDeclaration = Declare("first");
        var secondDeclaration = Declare("second");
        var thirdDeclaration = Declare("third");
        var first = new ThrowingProvider(firstDeclaration, firstException);
        var second = new AsyncDisposableProvider(secondDeclaration);
        var third = new AsyncDisposableProvider(thirdDeclaration);
        var firstKeyspace = new Keyspace("app", "first");
        var secondKeyspace = new Keyspace("app", "second");
        var thirdKeyspace = new Keyspace("app", "third");
        var firstBinding = new ProviderBinding("first") { Keyspace = firstKeyspace };
        var secondBinding = new ProviderBinding("second") { Keyspace = secondKeyspace };
        var thirdBinding = new ProviderBinding("third") { Keyspace = thirdKeyspace };
        var registrations = Register(first, firstBinding);
        registrations.ProviderFactories.Add(_ => second);
        registrations.ProviderFactories.Add(_ => third);
        registrations.Bindings.Add(secondBinding);
        registrations.Bindings.Add(thirdBinding);
        var composition = new CoordinationComposition(registrations, Services);
        await Assert.ThrowsAsync<CoordinationCompositionException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            composition.DisposeAsync().AsTask()
        );

        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, third.Disposals);
    }

    [Fact]
    public async Task DisposeAsync_WhenSeveralDisposalsThrow_ThrowsAggregate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstException = new InvalidOperationException("first");
        var secondException = new ArgumentException("second");
        var firstDeclaration = Declare("first");
        var secondDeclaration = Declare("second");
        var thirdDeclaration = Declare("third");
        var first = new ThrowingProvider(firstDeclaration, firstException);
        var second = new ThrowingProvider(secondDeclaration, secondException);
        var third = new AsyncDisposableProvider(thirdDeclaration);
        var firstKeyspace = new Keyspace("app", "first");
        var secondKeyspace = new Keyspace("app", "second");
        var thirdKeyspace = new Keyspace("app", "third");
        var firstBinding = new ProviderBinding("first") { Keyspace = firstKeyspace };
        var secondBinding = new ProviderBinding("second") { Keyspace = secondKeyspace };
        var thirdBinding = new ProviderBinding("third") { Keyspace = thirdKeyspace };
        var registrations = Register(first, firstBinding);
        registrations.ProviderFactories.Add(_ => second);
        registrations.ProviderFactories.Add(_ => third);
        registrations.Bindings.Add(secondBinding);
        registrations.Bindings.Add(thirdBinding);
        var composition = new CoordinationComposition(registrations, Services);
        await Assert.ThrowsAsync<CoordinationCompositionException>(async () =>
            await composition.EnsureStartedAsync(cancellationToken)
        );

        var aggregate = await Assert.ThrowsAsync<AggregateException>(() =>
            composition.DisposeAsync().AsTask()
        );

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(aggregate.InnerExceptions[0]);
        Assert.IsType<ArgumentException>(aggregate.InnerExceptions[1]);
        Assert.Equal(1, third.Disposals);
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

    #region Dispose Tests

    [Fact]
    public async Task Dispose_WhenComposed_DisposesOnlyTheSynchronousProviders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = Mocks.Create<IOccurrenceService>().Object;
        var syncDeclaration = Declare("sync");
        var asyncDeclaration = Declare("async");
        var sync = new DisposableProvider(syncDeclaration, service);
        var async = new AsyncDisposableProvider(asyncDeclaration);
        var binding = new ProviderBinding("sync");
        var registrations = Register(sync, binding);
        registrations.ProviderFactories.Add(_ => async);
        var composition = new CoordinationComposition(registrations, Services);
        await composition.EnsureStartedAsync(cancellationToken);

        composition.Dispose();

        Assert.Equal(1, sync.Disposals);
        Assert.Equal(0, async.Disposals);
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

    private sealed class ThrowingProvider(ProviderDeclaration declaration, Exception exception)
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
            return ValueTask.FromException(exception);
        }
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
