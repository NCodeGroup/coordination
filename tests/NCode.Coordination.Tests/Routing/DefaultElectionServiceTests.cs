// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultElectionServiceTests : IDisposable
{
    private static LeaseTerms Terms()
    {
        var ttl = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(ttl);
        return LeaseTerms.ForTimeToLive(timeToLive);
    }

    private static Leadership Leadership()
    {
        var token = new FencingToken(5);
        ReadOnlyMemory<byte> value = (byte[])[1];
        var leader = new Leader(TestComposition.Key, token, value);
        var owner = new OwnerToken("o-1");
        var terms = Terms();
        return new Leadership(leader, owner, terms);
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region TryLeadAsync Tests

    [Fact]
    public async Task TryLeadAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var terms = Terms();
        ReadOnlyMemory<byte> value = (byte[])[1, 2];
        var leadership = Leadership();
        var expected = ElectionResult.Elected(leadership);
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
        var terms = Terms();
        var mock = Mocks.Create<IElectionService>();
        await using var provider = TestComposition.BuildElsewhere(Primitives.Election, mock.Object);
        var service = provider.GetRequiredService<IElectionService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryLeadAsync(
                TestComposition.Key,
                ReadOnlyMemory<byte>.Empty,
                terms,
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
        var ttl = TimeSpan.FromSeconds(60);
        var timeToLive = new TimeToLive(ttl);
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
        var stream = TestComposition.Stream(view);
        var mock = Mocks.Create<IElectionService>();
        mock.Setup(x => x.WatchAsync(TestComposition.Key, null, cancellationToken))
            .Returns(stream)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Election, mock.Object);
        var service = provider.GetRequiredService<IElectionService>();
        var items = new List<ElectionView>();

        await foreach (var item in service.WatchAsync(TestComposition.Key, null, cancellationToken))
        {
            items.Add(item);
        }

        var single = Assert.Single(items);
        Assert.Same(view, single);
    }

    #endregion
}
