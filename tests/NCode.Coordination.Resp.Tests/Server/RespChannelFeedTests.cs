// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
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
        using var source = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RespChannelFeed.OpenAsync(multiplexer.Object, channel, schedule, source.Token).AsTask()
        );
    }

    #endregion
}
