// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespSubscriptionsTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region OpenAsync Tests

    [Fact]
    public async Task OpenAsync_WhenTheSubscribeFails_ThrowsTheTranslatedFailureAndDetachesTheHandlers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new RedisConnectionException(
            ConnectionFailureType.SocketFailure,
            CommandFlags.None,
            "The test dropped the connection.",
            null,
            CommandStatus.WaitingToBeSent
        );
        var subscriber = Mocks.Create<ISubscriber>();
        subscriber
            .Setup(x => x.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(failure)
            .Verifiable();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .SetupAdd(x =>
                x.ConnectionFailed += It.IsAny<EventHandler<ConnectionFailedEventArgs>>()
            )
            .Verifiable();
        multiplexer
            .SetupAdd(x =>
                x.ConnectionRestored += It.IsAny<EventHandler<ConnectionFailedEventArgs>>()
            )
            .Verifiable();
        multiplexer
            .SetupRemove(x =>
                x.ConnectionFailed -= It.IsAny<EventHandler<ConnectionFailedEventArgs>>()
            )
            .Verifiable();
        multiplexer
            .SetupRemove(x =>
                x.ConnectionRestored -= It.IsAny<EventHandler<ConnectionFailedEventArgs>>()
            )
            .Verifiable();
        multiplexer
            .Setup(x => x.GetSubscriber(It.IsAny<object?>()))
            .Returns(subscriber.Object)
            .Verifiable();
        var connection = new RespConnection(
            _ => ValueTask.FromResult(multiplexer.Object),
            owned: false
        );
        var checkInterval = RespSubscriptions.DefaultCheckInterval;
        var schedule = new RespCheckSchedule(checkInterval, TimeProvider.System);
        var subscriptions = new RespSubscriptions(connection, RespProfiles.Valkey, schedule);
        Func<CancellationToken, ValueTask<long>> readNumber = _ => ValueTask.FromResult(0L);

        var exception = await Assert.ThrowsAsync<CoordinationException>(() =>
            subscriptions.OpenAsync("channel", readNumber, cancellationToken).AsTask()
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
    }

    #endregion
}
