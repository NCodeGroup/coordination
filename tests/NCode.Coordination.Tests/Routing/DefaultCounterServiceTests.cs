// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultCounterServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region IncrementAsync Tests

    [Fact]
    public async Task IncrementAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(1));
        var mock = Mocks.Create<ICounterService>();
        mock.Setup(x =>
                x.IncrementAsync(TestComposition.Key, 2, timeToLive, null, cancellationToken)
            )
            .ReturnsAsync(7L)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Counter, mock.Object);
        var service = provider.GetRequiredService<ICounterService>();

        var result = await service.IncrementAsync(
            TestComposition.Key,
            2,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.Equal(7L, result);
    }

    [Fact]
    public async Task IncrementAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(1));
        var mock = Mocks.Create<ICounterService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<ICounterService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.IncrementAsync(
                TestComposition.Key,
                2,
                timeToLive,
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ICounterService>();
        mock.Setup(x => x.ReadAsync(TestComposition.Key, null, cancellationToken))
            .ReturnsAsync(5L)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Counter, mock.Object);
        var service = provider.GetRequiredService<ICounterService>();

        var result = await service.ReadAsync(TestComposition.Key, null, cancellationToken);

        Assert.Equal(5L, result);
    }

    #endregion

    #region ResetAsync Tests

    [Fact]
    public async Task ResetAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ICounterService>();
        mock.Setup(x => x.ResetAsync(TestComposition.Key, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Counter, mock.Object);
        var service = provider.GetRequiredService<ICounterService>();

        await service.ResetAsync(TestComposition.Key, null, cancellationToken);
    }

    #endregion

    #region IncrementBatchAsync Tests

    [Fact]
    public async Task IncrementBatchAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(1));
        IReadOnlyList<CounterIncrement> increments =
        [
            new CounterIncrement(TestComposition.Key, 1, 10, timeToLive),
        ];
        var expected = new CounterBatchResult([3]);
        var mock = Mocks.Create<ICounterService>();
        mock.Setup(x => x.IncrementBatchAsync(increments, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Counter, mock.Object);
        var service = provider.GetRequiredService<ICounterService>();

        var result = await service.IncrementBatchAsync(increments, null, cancellationToken);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task IncrementBatchAsync_WhenEmpty_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ICounterService>();
        await using var provider = TestComposition.Build(Primitives.Counter, mock.Object);
        var service = provider.GetRequiredService<ICounterService>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.IncrementBatchAsync([], null, cancellationToken)
        );

        Assert.Equal("increments", exception.ParamName);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(ICounterService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Counter)
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
