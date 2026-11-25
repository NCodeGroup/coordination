// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultLeaseServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private static LeaseTerms Terms() =>
        LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)));

    private static LeaseGrant Grant() =>
        new(TestComposition.Key, new FencingToken(5), new OwnerToken("o-1"), Terms());

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var terms = Terms();
        var expected = LeaseAcquireResult.Acquired(Grant());
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
        var mock = Mocks.Create<ILeaseService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<ILeaseService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryAcquireAsync(TestComposition.Key, Terms(), null, cancellationToken)
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
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(60));
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

    private static ServiceProvider BuildElsewhere(ILeaseService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Lease)
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
