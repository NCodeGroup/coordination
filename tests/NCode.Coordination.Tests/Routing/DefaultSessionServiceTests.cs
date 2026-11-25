// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultSessionServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region OpenAsync Tests

    [Fact]
    public async Task OpenAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));
        var grant = new SessionGrant(new SessionId(TestComposition.Keyspace, "1"), timeToLive);
        var mock = Mocks.Create<ISessionService>();
        mock.Setup(x => x.OpenAsync(TestComposition.Keyspace, timeToLive, null, cancellationToken))
            .ReturnsAsync(grant)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Session, mock.Object);
        var service = provider.GetRequiredService<ISessionService>();

        var result = await service.OpenAsync(
            TestComposition.Keyspace,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.Same(grant, result);
    }

    [Fact]
    public async Task OpenAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ISessionService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<ISessionService>();
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.OpenAsync(TestComposition.Keyspace, timeToLive, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region KeepAliveAsync Tests

    [Fact]
    public async Task KeepAliveAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = new SessionId(TestComposition.Keyspace, "1");
        var expected = KeepAliveResult.Renewed(new TimeToLive(TimeSpan.FromSeconds(30)));
        var mock = Mocks.Create<ISessionService>();
        mock.Setup(x => x.KeepAliveAsync(id, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Session, mock.Object);
        var service = provider.GetRequiredService<ISessionService>();

        var result = await service.KeepAliveAsync(id, null, cancellationToken);

        Assert.Same(expected, result);
    }

    #endregion

    #region CloseAsync Tests

    [Fact]
    public async Task CloseAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = new SessionId(TestComposition.Keyspace, "1");
        var mock = Mocks.Create<ISessionService>();
        mock.Setup(x => x.CloseAsync(id, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Session, mock.Object);
        var service = provider.GetRequiredService<ISessionService>();

        await service.CloseAsync(id, null, cancellationToken);
    }

    #endregion

    #region WaitForEndAsync Tests

    [Fact]
    public async Task WaitForEndAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = new SessionId(TestComposition.Keyspace, "1");
        var mock = Mocks.Create<ISessionService>();
        mock.Setup(x => x.WaitForEndAsync(id, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Session, mock.Object);
        var service = provider.GetRequiredService<ISessionService>();

        await service.WaitForEndAsync(id, null, cancellationToken);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(ISessionService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Session)
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
