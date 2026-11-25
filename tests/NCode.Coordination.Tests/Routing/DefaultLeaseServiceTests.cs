// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultLeaseServiceTests : IDisposable
{
    private static LeaseTerms Terms()
    {
        var ttl = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(ttl);
        return LeaseTerms.ForTimeToLive(timeToLive);
    }

    private static LeaseGrant Grant()
    {
        var token = new FencingToken(5);
        var owner = new OwnerToken("o-1");
        var terms = Terms();
        return new LeaseGrant(TestComposition.Key, token, owner, terms);
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var terms = Terms();
        var grant = Grant();
        var expected = LeaseAcquireResult.Acquired(grant);
        var mock = Mocks.Create<ILeaseService>();
        mock.Setup(x => x.TryAcquireAsync(TestComposition.Key, terms, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Lease, mock.Object);
        var service = provider.GetRequiredService<ILeaseService>();

        var result = await service.TryAcquireAsync(
            TestComposition.Key,
            terms,
            null,
            cancellationToken
        );

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var terms = Terms();
        var mock = Mocks.Create<ILeaseService>();
        await using var provider = TestComposition.BuildElsewhere(Primitives.Lease, mock.Object);
        var service = provider.GetRequiredService<ILeaseService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryAcquireAsync(TestComposition.Key, terms, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region RenewAsync Tests

    [Fact]
    public async Task RenewAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grant = Grant();
        var ttl = TimeSpan.FromSeconds(60);
        var timeToLive = new TimeToLive(ttl);
        var mock = Mocks.Create<ILeaseService>();
        mock.Setup(x => x.RenewAsync(grant, timeToLive, null, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Lease, mock.Object);
        var service = provider.GetRequiredService<ILeaseService>();

        var result = await service.RenewAsync(grant, timeToLive, null, cancellationToken);

        Assert.True(result);
    }

    #endregion

    #region ReleaseAsync Tests

    [Fact]
    public async Task ReleaseAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grant = Grant();
        var mock = Mocks.Create<ILeaseService>();
        mock.Setup(x => x.ReleaseAsync(grant, null, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Lease, mock.Object);
        var service = provider.GetRequiredService<ILeaseService>();

        var result = await service.ReleaseAsync(grant, null, cancellationToken);

        Assert.True(result);
    }

    #endregion

    #region WaitForReleaseAsync Tests

    [Fact]
    public async Task WaitForReleaseAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ILeaseService>();
        mock.Setup(x => x.WaitForReleaseAsync(TestComposition.Key, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Lease, mock.Object);
        var service = provider.GetRequiredService<ILeaseService>();

        await service.WaitForReleaseAsync(TestComposition.Key, null, cancellationToken);
    }

    #endregion
}
