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

    private (RespProvider Provider, Mock<IDatabase> Database) CreateProvider()
    {
        var database = Mocks.Create<IDatabase>();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var connection = new RespConnection(_ => Task.FromResult(multiplexer.Object), owned: false);

        return (new RespProvider(connection, RespProfiles.Valkey), database);
    }

    [Fact]
    public async Task Declaration_Always_NamesRespAndProfile()
    {
        var (provider, _) = CreateProvider();
        await using var owned = provider;

        var declaration = provider.Declaration;

        Assert.Equal("resp", declaration.Provider);
        Assert.Equal("valkey", declaration.Profile);
        Assert.Empty(declaration.Primitives);
    }

    [Fact]
    public async Task GetPrimitiveService_AnyType_ReturnsNull()
    {
        var (provider, _) = CreateProvider();
        await using var owned = provider;

        var service = provider.GetPrimitiveService(typeof(ILeaseService));

        Assert.Null(service);
    }

    [Fact]
    public async Task ProbeAsync_OtherServerName_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (provider, database) = CreateProvider();
        await using var owned = provider;
        var reply = RedisResult.Create(
            new RedisValue("# Server\r\nserver_name:garnet\r\ngarnet_version:2.2.13\r\n")
        );
        database
            .Setup(x => x.ExecuteAsync("INFO", It.IsAny<object[]>()))
            .Returns(Task.FromResult(reply))
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
        var (provider, database) = CreateProvider();
        await using var owned = provider;
        var reply = RedisResult.Create(
            new RedisValue("# Server\r\nserver_name:valkey\r\nvalkey_version:7.0.0\r\n")
        );
        database
            .Setup(x => x.ExecuteAsync("INFO", It.IsAny<object[]>()))
            .Returns(Task.FromResult(reply))
            .Verifiable();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains("'7.0.0'", exception.Message);
    }
}
