// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NCode.Coordination.Resp.Logging;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespChannelFeedTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region OpenAsync Tests

    [Fact]
    public async Task OpenAsync_WhenTheTokenIsCanceled_ThrowsBeforeSubscribing()
    {
        var testToken = TestContext.Current.CancellationToken;
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        var channel = RedisChannel.Literal("channel");
        var interval = TimeSpan.FromSeconds(5);
        var schedule = new RespCheckSchedule(interval, TimeProvider.System);
        var logger = NullLogger<RespChannelFeed>.Instance;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RespChannelFeed
                .OpenAsync(multiplexer.Object, channel, schedule, logger, source.Token)
                .AsTask()
        );
    }

    #endregion

    #region TryUnsubscribeAsync Tests

    [Fact]
    public async Task TryUnsubscribeAsync_WhenTheConnectionIsDisposed_LogsAtDebugOnce()
    {
        var logger = new FakeLogger<RespChannelFeed>();
        var failure = new ObjectDisposedException("multiplexer");

        var unsubscribed = await RespChannelFeed.TryUnsubscribeAsync(
            () => Task.FromException(failure),
            logger
        );

        Assert.False(unsubscribed);
        var records = logger.Collector.GetSnapshot();
        var record = Assert.Single(records);
        Assert.Equal(EventIds.UnsubscribeFailed, record.Id.Id);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Same(failure, record.Exception);
    }

    #endregion
}
