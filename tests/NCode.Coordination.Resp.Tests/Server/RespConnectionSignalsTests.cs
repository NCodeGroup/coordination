// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Threading.Channels;
using Moq;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespConnectionSignalsTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenAConnectionFailsAndIsRestored_WritesBothSignals()
    {
        EventHandler<ConnectionFailedEventArgs>? failed = null;
        EventHandler<ConnectionFailedEventArgs>? restored = null;
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .SetupAdd(x =>
                x.ConnectionFailed += It.IsAny<EventHandler<ConnectionFailedEventArgs>>()
            )
            .Callback<EventHandler<ConnectionFailedEventArgs>>(h => failed = h)
            .Verifiable();
        multiplexer
            .SetupAdd(x =>
                x.ConnectionRestored += It.IsAny<EventHandler<ConnectionFailedEventArgs>>()
            )
            .Callback<EventHandler<ConnectionFailedEventArgs>>(h => restored = h)
            .Verifiable();
        var channel = Channel.CreateUnbounded<RespSignal>();

        using (var signals = new RespConnectionSignals(multiplexer.Object, channel.Writer))
        {
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
            Assert.NotNull(failed);
            Assert.NotNull(restored);
            var args = new ConnectionFailedEventArgs(
                multiplexer.Object,
                new DnsEndPoint("localhost", 6379),
                ConnectionType.Subscription,
                ConnectionFailureType.SocketFailure,
                new InvalidOperationException("The test dropped the connection."),
                "test"
            );

            failed(multiplexer.Object, args);
            restored(multiplexer.Object, args);

            Assert.True(channel.Reader.TryRead(out var first));
            Assert.True(channel.Reader.TryRead(out var second));
            Assert.False(channel.Reader.TryRead(out _));
            Assert.Equal(RespSignalKind.Failed, first.Kind);
            Assert.Equal(RespSignalKind.Restored, second.Kind);
        }
    }

    #endregion
}
