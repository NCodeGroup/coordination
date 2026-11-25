// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultElectionServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private static LeaseTerms Terms() =>
        LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)));

    private static Leadership Leadership() =>
        new(
            new Leader(TestComposition.Key, new FencingToken(5), new byte[] { 1 }),
            new OwnerToken("o-1"),
            Terms()
        );

    #region TryLeadAsync Tests

    [Fact]
    public async Task TryLeadAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var terms = Terms();
        ReadOnlyMemory<byte> value = new byte[] { 1, 2 };
        var expected = ElectionResult.Elected(Leadership());
        var mock = Mocks.Create<IElectionService>();
        mock.Setup(x => x.TryLeadAsync(TestComposition.Key, value, terms, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Election, mock.Object);
        var service = provider.GetRequiredService<IElectionService>();

        var result = await service.TryLeadAsync(
            TestComposition.Key,
            value,
            terms,
            null,
            cancellationToken
        );

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task TryLeadAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<IElectionService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<IElectionService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryLeadAsync(
                TestComposition.Key,
                ReadOnlyMemory<byte>.Empty,
                Terms(),
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task RenewAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var leadership = Leadership();
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(60));
        var mock = Mocks.Create<IElectionService>();
        mock.Setup(x => x.RenewAsync(leadership, timeToLive, null, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Election, mock.Object);
        var service = provider.GetRequiredService<IElectionService>();

        var result = await service.RenewAsync(leadership, timeToLive, null, cancellationToken);

        Assert.True(result);
    }

    #endregion

    #region ResignAsync Tests

    [Fact]
    public async Task ResignAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var leadership = Leadership();
        var mock = Mocks.Create<IElectionService>();
        mock.Setup(x => x.ResignAsync(leadership, null, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Election, mock.Object);
        var service = provider.GetRequiredService<IElectionService>();

        var result = await service.ResignAsync(leadership, null, cancellationToken);

        Assert.True(result);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new ElectionView(TestComposition.Key, null);
        var mock = Mocks.Create<IElectionService>();
        mock.Setup(x => x.ReadAsync(TestComposition.Key, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Election, mock.Object);
        var service = provider.GetRequiredService<IElectionService>();

        var result = await service.ReadAsync(TestComposition.Key, null, cancellationToken);

        Assert.Same(expected, result);
    }

    #endregion

    #region WatchAsync Tests

    [Fact]
    public async Task WatchAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var view = new ElectionView(TestComposition.Key, null);
        var mock = Mocks.Create<IElectionService>();
        mock.Setup(x => x.WatchAsync(TestComposition.Key, null, cancellationToken))
            .Returns(TestComposition.Stream(view))
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Election, mock.Object);
        var service = provider.GetRequiredService<IElectionService>();
        var items = new List<ElectionView>();

        await foreach (var item in service.WatchAsync(TestComposition.Key, null, cancellationToken))
        {
            items.Add(item);
        }

        Assert.Same(view, Assert.Single(items));
    }

    #endregion

    private static ServiceProvider BuildElsewhere(IElectionService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Election)
        );
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddCoordination(b =>
            b.AddProvider(_ => new TestProvider(declaration, implementation))
                .Bind(
                    new ProviderBinding(TestComposition.ProviderName)
                    {
                        Keyspace = new Keyspace("elsewhere"),
                    }
                )
        );
        return services.BuildServiceProvider();
    }
}
