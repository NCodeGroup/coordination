// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests;

public sealed class RespProviderTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private RespProvider CreateProvider()
    {
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        var connection = new RespConnection(
            _ => ValueTask.FromResult(multiplexer.Object),
            owned: false
        );

        return new RespProvider(connection, RespProfiles.Valkey);
    }

    private RespProvider CreateProvider(Mock<IDatabase> database)
    {
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object)
            .Verifiable();
        var connection = new RespConnection(
            _ => ValueTask.FromResult(multiplexer.Object),
            owned: false
        );

        return new RespProvider(connection, RespProfiles.Valkey);
    }

    #region Declaration Property Tests

    [Fact]
    public async Task Declaration_Always_NamesRespAndProfile()
    {
        await using var provider = CreateProvider();

        var declaration = provider.Declaration;

        Assert.Equal("resp", declaration.Provider);
        Assert.Equal("valkey", declaration.Profile);
        Assert.Empty(declaration.Primitives);
    }

    #endregion

    #region GetPrimitiveService Tests

    [Fact]
    public async Task GetPrimitiveService_AnyType_ReturnsNull()
    {
        await using var provider = CreateProvider();

        var service = provider.GetPrimitiveService(typeof(ILeaseService));

        Assert.Null(service);
    }

    #endregion

    #region ProbeAsync Tests

    [Fact]
    public async Task ProbeAsync_OtherServerName_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        await using var provider = CreateProvider(database);
        var info = new RedisValue("# Server\r\nserver_name:garnet\r\ngarnet_version:2.2.13\r\n");
        var reply = RedisResult.Create(info);
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    It.IsAny<CommandFlags>()
                )
            )
            .ReturnsAsync(reply)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains("'garnet'", exception.Message);
    }

    [Fact]
    public async Task ProbeAsync_OldVersion_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        await using var provider = CreateProvider(database);
        var info = new RedisValue("# Server\r\nserver_name:valkey\r\nvalkey_version:7.0.0\r\n");
        var reply = RedisResult.Create(info);
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    It.IsAny<CommandFlags>()
                )
            )
            .ReturnsAsync(reply)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains("'7.0.0'", exception.Message);
    }

    [Fact]
    public async Task ProbeAsync_ReplyIsNotText_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        await using var provider = CreateProvider(database);
        RedisValue[] values = [new("1"), new("2")];
        var reply = RedisResult.Create(values);
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    It.IsAny<CommandFlags>()
                )
            )
            .ReturnsAsync(reply)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(
            "The RESP server answered the probe script with an unexpected reply.",
            exception.Message
        );
    }

    [Fact]
    public async Task ProbeAsync_ServerLoading_ThrowsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        await using var provider = CreateProvider(database);
        var failure = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "LOADING the server is loading the dataset in memory"
        );
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    It.IsAny<CommandFlags>()
                )
            )
            .ThrowsAsync(failure)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task ProbeAsync_ScriptingDisabled_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        await using var provider = CreateProvider(database);
        var failure = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "ERR This instance has Lua scripting support disabled"
        );
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    It.IsAny<CommandFlags>()
                )
            )
            .ThrowsAsync(failure)
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Same(failure, exception.InnerException);
        Assert.StartsWith(
            "The RESP server refused the provider's probe script:",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    #endregion
}
