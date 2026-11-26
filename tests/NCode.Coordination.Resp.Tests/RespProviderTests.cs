// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests;

public sealed class RespProviderTests(RespServers servers) : IDisposable
{
    private static RespProvider CreateProvider(string endpoint, RespProfile profile)
    {
        var connection = new RespConnection(
            async ct =>
            {
                var connecting = ConnectionMultiplexer.ConnectAsync(endpoint);
                return await connecting.WaitAsync(ct);
            },
            owned: true
        );
        return new RespProvider(connection, profile);
    }

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
    }

    [Fact]
    public async Task Declaration_Always_DeclaresOccurrencesSetsCountersAndSequences()
    {
        await using var provider = CreateProvider();

        var primitives = provider.Declaration.Primitives;

        Assert.Collection(
            primitives,
            support =>
            {
                Assert.Equal(Primitives.Occurrence, support.Primitive);
                Assert.Equal<Guarantee>(
                    [
                        Guarantees.LinearizableUntilFailover,
                        Guarantees.Volatile,
                        Guarantees.ServerClock,
                    ],
                    support.Guarantees
                );
            },
            support =>
            {
                Assert.Equal(Primitives.Set, support.Primitive);
                Assert.Equal<Guarantee>(
                    [
                        Guarantees.LinearizableUntilFailover,
                        Guarantees.Volatile,
                        Guarantees.ServerClock,
                    ],
                    support.Guarantees
                );
            },
            support =>
            {
                Assert.Equal(Primitives.Counter, support.Primitive);
                Assert.Equal<Guarantee>(
                    [
                        Guarantees.LinearizableUntilFailover,
                        Guarantees.Volatile,
                        Guarantees.ServerClock,
                        Guarantees.AtomicAcrossPartitions,
                    ],
                    support.Guarantees
                );
            },
            support =>
            {
                Assert.Equal(Primitives.Sequence, support.Primitive);
                Assert.Equal<Guarantee>(
                    [Guarantees.LinearizableUntilFailover, Guarantees.Volatile],
                    support.Guarantees
                );
            }
        );
    }

    #endregion

    #region GetPrimitiveService Tests

    [Fact]
    public async Task GetPrimitiveService_UnservedPrimitive_ReturnsNull()
    {
        await using var provider = CreateProvider();

        var service = provider.GetPrimitiveService(typeof(ILeaseService));

        Assert.Null(service);
    }

    [Theory]
    [InlineData(typeof(IOccurrenceService))]
    [InlineData(typeof(ISetService))]
    [InlineData(typeof(ICounterService))]
    [InlineData(typeof(ISequenceService))]
    public async Task GetPrimitiveService_ServedPrimitive_ReturnsItsService(Type serviceType)
    {
        await using var provider = CreateProvider();

        var service = provider.GetPrimitiveService(serviceType);

        Assert.NotNull(service);
        Assert.IsAssignableFrom(serviceType, service);
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
    public async Task ProbeAsync_IntegerReply_ThrowsUnsupportedNamingItsType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        await using var provider = CreateProvider(database);
        RedisValue value = 42;
        var reply = RedisResult.Create(value, ResultType.Integer);
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
            "The RESP server answered the probe script with a reply of type Integer, not text.",
            exception.Message
        );
    }

    [Fact]
    public async Task ProbeAsync_NullReply_ThrowsUnsupportedNamingNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = Mocks.Create<IDatabase>();
        await using var provider = CreateProvider(database);
        var reply = RedisResult.Create(RedisValue.Null);
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
            "The RESP server answered the probe script with a reply of type null, not text.",
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

    [RespServerFact]
    public async Task ProbeAsync_ValkeyWithValkeyProfile_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.Valkey, RespProfiles.Valkey);

        await provider.ProbeAsync(cancellationToken);

        Assert.Equal("valkey", provider.Declaration.Profile);
    }

    [RespServerFact]
    public async Task ProbeAsync_GarnetWithGarnetProfile_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.Garnet, RespProfiles.Garnet);

        await provider.ProbeAsync(cancellationToken);

        Assert.Equal("garnet", provider.Declaration.Profile);
    }

    [RespServerFact]
    public async Task ProbeAsync_ValkeyWithGarnetProfile_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.Valkey, RespProfiles.Garnet);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(
            "The RESP server is 'valkey', but the provider uses the 'garnet' profile.",
            exception.Message
        );
    }

    [RespServerFact]
    public async Task ProbeAsync_GarnetWithoutLua_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.GarnetWithoutLua, RespProfiles.Garnet);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.StartsWith(
            "The RESP server refused the provider's probe script:",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [RespServerFact]
    public async Task ProbeAsync_UserWithoutEval_ThrowsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var admin = await ConnectionMultiplexer.ConnectAsync(servers.Valkey);
        await admin
            .GetDatabase()
            .ExecuteAsync(
                "ACL",
                "SETUSER",
                "probe-limited",
                "on",
                ">probe-limited-pass",
                "~*",
                "&*",
                "+@all",
                "-eval",
                "-evalsha"
            );
        await using var provider = CreateProvider(
            $"{servers.Valkey},user=probe-limited,password=probe-limited-pass",
            RespProfiles.Valkey
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.AccessDenied, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
