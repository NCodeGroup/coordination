// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultSetServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = KeySegment.FromString("member");
        var ttl = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(ttl);
        var mock = Mocks.Create<ISetService>();
        mock.Setup(x =>
                x.AddAsync(TestComposition.Key, member, timeToLive, null, cancellationToken)
            )
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Set, mock.Object);
        var service = provider.GetRequiredService<ISetService>();

        var result = await service.AddAsync(
            TestComposition.Key,
            member,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.True(result);
    }

    [Fact]
    public async Task AddAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = KeySegment.FromString("member");
        var ttl = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(ttl);
        var mock = Mocks.Create<ISetService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<ISetService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.AddAsync(TestComposition.Key, member, timeToLive, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = KeySegment.FromString("member");
        var mock = Mocks.Create<ISetService>();
        mock.Setup(x => x.RemoveAsync(TestComposition.Key, member, null, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Set, mock.Object);
        var service = provider.GetRequiredService<ISetService>();

        var result = await service.RemoveAsync(
            TestComposition.Key,
            member,
            null,
            cancellationToken
        );

        Assert.True(result);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = KeySegment.FromString("member");
        ImmutableArray<KeySegment> members = [member];
        var mock = Mocks.Create<ISetService>();
        mock.Setup(x => x.ListAsync(TestComposition.Key, null, cancellationToken))
            .ReturnsAsync(members)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Set, mock.Object);
        var service = provider.GetRequiredService<ISetService>();

        var result = await service.ListAsync(TestComposition.Key, null, cancellationToken);

        Assert.Equal(members, result);
    }

    #endregion

    #region ClearAsync Tests

    [Fact]
    public async Task ClearAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ISetService>();
        mock.Setup(x => x.ClearAsync(TestComposition.Key, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Set, mock.Object);
        var service = provider.GetRequiredService<ISetService>();

        await service.ClearAsync(TestComposition.Key, null, cancellationToken);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(ISetService implementation)
    {
        var support = PrimitiveSupport.Supported(Primitives.Set);
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
