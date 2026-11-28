// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.PostgreSql.Logging;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgListenerTests
{
    private sealed class ThrowingDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            var failure = new InvalidOperationException("The disposal failed.");
            return ValueTask.FromException(failure);
        }
    }

    private static PgListener CreateListener(PgSource source)
    {
        return CreateListener(source, TimeProvider.System, NullLogger<PgListener>.Instance);
    }

    private static PgListener CreateListener(
        PgSource source,
        TimeProvider time,
        ILogger<PgListener> logger
    )
    {
        var events = new PgListenerEvents((_, _) => { }, () => { }, () => { });
        return new PgListener(source, events, time, logger);
    }

    #region ListenAsync Tests

    [Fact]
    public async Task ListenAsync_ChannelWithAQuote_Throws()
    {
        await using var source = PgSource.FromConnectionString(PgOfflineServer.ConnectionString);
        await using var listener = CreateListener(source);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await listener.ListenAsync("ncode.\"; DROP TABLE x; --", CancellationToken.None)
        );

        Assert.Equal("channel", exception.ParamName);
        Assert.StartsWith(PgListener.NotAChannelMessage, exception.Message);
    }

    [Fact]
    public async Task ListenAsync_ServerUnreachable_ThrowsUnavailable()
    {
        await using var source = PgSource.FromConnectionString(PgOfflineServer.ConnectionString);
        await using var listener = CreateListener(source);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await listener.ListenAsync("ncode.0123abcd.0123456789abcdef", CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task ListenAsync_ServerUnreachable_LogsConnectionFailedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(PgOfflineServer.ConnectionString);
        var time = new FakeTimeProvider();
        var logger = new FakeLogger<PgListener>();
        await using var listener = CreateListener(source, time, logger);

        await Assert.ThrowsAsync<CoordinationException>(async () =>
            await listener.ListenAsync("ncode.0123abcd.0123456789abcdef", cancellationToken)
        );

        var records = logger.Collector.GetSnapshot();
        var record = Assert.Single(records, x => x.Id.Id == EventIds.ListenerConnectionFailed);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.NotNull(record.Exception);
    }

    #endregion

    #region DisposeQuietlyAsync Tests

    [Fact]
    public async Task DisposeQuietlyAsync_DisposalThrows_LogsAtDebugOnce()
    {
        var logger = new FakeLogger<PgListener>();
        var connection = new ThrowingDisposable();

        await PgListener.DisposeQuietlyAsync(connection, logger);

        var records = logger.Collector.GetSnapshot();
        var record = Assert.Single(records, x => x.Id.Id == EventIds.ListenerDisposeFailed);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.IsType<InvalidOperationException>(record.Exception);
    }

    #endregion
}
