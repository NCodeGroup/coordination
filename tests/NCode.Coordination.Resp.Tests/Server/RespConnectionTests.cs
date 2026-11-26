// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespConnectionTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_CalledTwice_ConnectsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        var connects = 0;
        await using var connection = new RespConnection(
            _ =>
            {
                connects++;
                return ValueTask.FromResult(multiplexer.Object);
            },
            owned: false
        );

        var first = await connection.GetAsync(cancellationToken);
        var second = await connection.GetAsync(cancellationToken);

        Assert.Same(multiplexer.Object, first);
        Assert.Same(multiplexer.Object, second);
        Assert.Equal(1, connects);
    }

    [Fact]
    public async Task GetAsync_ConnectFails_ThrowsUnavailableNotApplied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            CommandFlags.None,
            "down",
            null,
            CommandStatus.Unknown
        );
        await using var connection = new RespConnection(
            _ => ValueTask.FromException<IConnectionMultiplexer>(failure),
            owned: false
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.GetAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
        Assert.Equal("The RESP provider could not connect to its server: down", exception.Message);
    }

    [Fact]
    public async Task GetAsync_AfterFailedConnect_ConnectsAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        var failure = new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            CommandFlags.None,
            "down",
            null,
            CommandStatus.Unknown
        );
        var connects = 0;
        await using var connection = new RespConnection(
            _ =>
            {
                connects++;
                if (connects == 1)
                {
                    return ValueTask.FromException<IConnectionMultiplexer>(failure);
                }

                return ValueTask.FromResult(multiplexer.Object);
            },
            owned: false
        );
        await Assert.ThrowsAsync<CoordinationException>(async () =>
            await connection.GetAsync(cancellationToken)
        );

        var result = await connection.GetAsync(cancellationToken);

        Assert.Same(multiplexer.Object, result);
        Assert.Equal(2, connects);
    }

    [Fact]
    public async Task GetAsync_DisposedWhileConnecting_DisposesOwnedMultiplexerAndThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = new TaskCompletionSource<IConnectionMultiplexer>();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var connection = new RespConnection(
            _ => new ValueTask<IConnectionMultiplexer>(source.Task),
            owned: true
        );
        var pending = connection.GetAsync(cancellationToken).AsTask();
        await connection.DisposeAsync();

        source.SetResult(multiplexer.Object);

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_Owned_DisposesMultiplexer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        var connection = new RespConnection(
            _ => ValueTask.FromResult(multiplexer.Object),
            owned: true
        );
        await connection.GetAsync(cancellationToken);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_Borrowed_LeavesMultiplexer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        var connection = new RespConnection(
            _ => ValueTask.FromResult(multiplexer.Object),
            owned: false
        );
        await connection.GetAsync(cancellationToken);

        await connection.DisposeAsync();
    }

    #endregion

    #region ConnectOwnedAsync Tests

    [Fact]
    public async Task ConnectOwnedAsync_CanceledBeforeConnect_DisposesLateMultiplexer()
    {
        var source = new TaskCompletionSource<IConnectionMultiplexer>();
        var disposed = new TaskCompletionSource();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .Setup(x => x.DisposeAsync())
            .Returns(() =>
            {
                disposed.SetResult();
                return ValueTask.CompletedTask;
            })
            .Verifiable();
        using var cts = new CancellationTokenSource();
        var pending = RespConnection.ConnectOwnedAsync(_ => source.Task, cts.Token).AsTask();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        var timeout = TimeSpan.FromSeconds(5);

        source.SetResult(multiplexer.Object);

        await disposed.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);
    }

    #endregion
}
