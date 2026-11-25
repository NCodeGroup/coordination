// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultSemaphoreServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private static LeaseTerms Terms()
    {
        var ttl = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(ttl);
        return LeaseTerms.ForTimeToLive(timeToLive);
    }

    private static SemaphoreGrant Grant()
    {
        var token = new FencingToken(5);
        var owner = new OwnerToken("o-1");
        var terms = Terms();
        return new SemaphoreGrant(TestComposition.Key, 2, token, owner, terms);
    }

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var terms = Terms();
        var grant = Grant();
        var expected = SemaphoreAcquireResult.Acquired(grant);
        var mock = Mocks.Create<ISemaphoreService>();
        mock.Setup(x => x.TryAcquireAsync(TestComposition.Key, 3, terms, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Semaphore, mock.Object);
        var service = provider.GetRequiredService<ISemaphoreService>();

        var result = await service.TryAcquireAsync(
            TestComposition.Key,
            3,
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
        var mock = Mocks.Create<ISemaphoreService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<ISemaphoreService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryAcquireAsync(TestComposition.Key, 3, terms, null, cancellationToken)
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
        var mock = Mocks.Create<ISemaphoreService>();
        mock.Setup(x => x.RenewAsync(grant, timeToLive, null, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Semaphore, mock.Object);
        var service = provider.GetRequiredService<ISemaphoreService>();

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
        var mock = Mocks.Create<ISemaphoreService>();
        mock.Setup(x => x.ReleaseAsync(grant, null, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Semaphore, mock.Object);
        var service = provider.GetRequiredService<ISemaphoreService>();

        var result = await service.ReleaseAsync(grant, null, cancellationToken);

        Assert.True(result);
    }

    #endregion

    #region WaitForReleaseAsync Tests

    [Fact]
    public async Task WaitForReleaseAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ISemaphoreService>();
        mock.Setup(x => x.WaitForReleaseAsync(TestComposition.Key, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Semaphore, mock.Object);
        var service = provider.GetRequiredService<ISemaphoreService>();

        await service.WaitForReleaseAsync(TestComposition.Key, null, cancellationToken);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(ISemaphoreService implementation)
    {
        var support = PrimitiveSupport.Supported(Primitives.Semaphore);
        var declaration = new ProviderDeclaration(TestComposition.ProviderName, support);
        var binding = new ProviderBinding(TestComposition.ProviderName)
        {
            Keyspace = new Keyspace("elsewhere"),
        };
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddCoordination(b =>
            b.AddProvider(_ => new TestProvider(declaration, implementation)).Bind(binding)
        );
        return services.BuildServiceProvider();
    }
}
