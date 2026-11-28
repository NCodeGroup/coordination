// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NCode.Coordination.Resp.Records;
using NCode.Coordination.Resp.Server;
using StackExchange.Redis;

namespace NCode.Coordination.Tests;

public sealed class RespCoordinationBuilderExtensionsTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region AddRespProvider Tests

    [Fact]
    public void AddRespProvider_NeitherSet_Throws()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Object.AddRespProvider(_ => { })
        );

        Assert.Equal("configure", exception.ParamName);
        Assert.StartsWith(
            "Set exactly one of RespProviderOptions.Configuration and RespProviderOptions.Connection.",
            exception.Message
        );
    }

    [Fact]
    public void AddRespProvider_BothSet_Throws()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Object.AddRespProvider(options =>
            {
                options.Configuration = "localhost:1";
                options.Connection = _ => throw new InvalidOperationException();
            })
        );

        Assert.Equal("configure", exception.ParamName);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-10_000L)]
    [InlineData(9_999L)]
    [InlineData(36_000_000_000L)]
    public void AddRespProvider_CheckIntervalOutOfRange_Throws(long ticks)
    {
        var builder = Mocks.Create<ICoordinationBuilder>();
        var checkInterval = TimeSpan.FromTicks(ticks);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.Object.AddRespProvider(options =>
            {
                options.Configuration = "localhost:1";
                options.CheckInterval = checkInterval;
            })
        );

        Assert.Equal("configure", exception.ParamName);
    }

    [Theory]
    [InlineData(10_000L)]
    [InlineData(35_999_999_999L)]
    public void AddRespProvider_CheckIntervalInRange_AddsTheProvider(long ticks)
    {
        var builder = Mocks.Create<ICoordinationBuilder>();
        builder
            .Setup(x => x.AddProvider(It.IsAny<Func<IServiceProvider, ICoordinationProvider>>()))
            .Returns(builder.Object)
            .Verifiable();
        var checkInterval = TimeSpan.FromTicks(ticks);

        var result = builder.Object.AddRespProvider(options =>
        {
            options.Configuration = "localhost:1";
            options.CheckInterval = checkInterval;
        });

        Assert.Same(builder.Object, result);
    }

    [Fact]
    public async Task AddRespProvider_Configuration_AddsProviderWithProfile()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        builder
            .Setup(x => x.AddProvider(It.IsAny<Func<IServiceProvider, ICoordinationProvider>>()))
            .Callback<Func<IServiceProvider, ICoordinationProvider>>(f => factory = f)
            .Returns(builder.Object)
            .Verifiable();
        await using var services = new ServiceCollection().BuildServiceProvider();

        var result = builder.Object.AddRespProvider(options =>
        {
            options.Configuration = "localhost:1";
            options.Profile = RespProfiles.Garnet;
        });

        Assert.Same(builder.Object, result);
        Assert.NotNull(factory);
        var provider = factory(services);
        await using var disposable = Assert.IsAssignableFrom<IAsyncDisposable>(provider);
        Assert.Equal("garnet", provider.Declaration.Profile);
    }

    [Fact]
    public async Task AddRespProvider_Connection_UsesHostMultiplexerAndLeavesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = Mocks.Create<ICoordinationBuilder>();
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        builder
            .Setup(x => x.AddProvider(It.IsAny<Func<IServiceProvider, ICoordinationProvider>>()))
            .Callback<Func<IServiceProvider, ICoordinationProvider>>(f => factory = f)
            .Returns(builder.Object)
            .Verifiable();
        var info = new RedisValue("# Server\r\nserver_name:valkey\r\nvalkey_version:9.2.0\r\n");
        var reply = RedisResult.Create(info);
        RedisResult[] noDenied = [];
        var permissions = RedisResult.Create(noDenied);
        var database = Mocks.Create<IDatabase>();
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.Is<string>(s => s == RespScript.Probe.Text),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    It.IsAny<CommandFlags>()
                )
            )
            .ReturnsAsync(reply)
            .Verifiable();
        database
            .Setup(x =>
                x.ScriptEvaluateAsync(
                    It.Is<string>(s => s == RespScript.ProbeCommands.Text),
                    It.IsAny<RedisKey[]?>(),
                    It.IsAny<RedisValue[]?>(),
                    It.IsAny<CommandFlags>()
                )
            )
            .ReturnsAsync(permissions)
            .Verifiable();
        var multiplexer = Mocks.Create<IConnectionMultiplexer>();
        multiplexer
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object)
            .Verifiable();
        await using var services = new ServiceCollection().BuildServiceProvider();
        IServiceProvider? received = null;

        builder.Object.AddRespProvider(options =>
            options.Connection = s =>
            {
                received = s;
                return multiplexer.Object;
            }
        );

        Assert.NotNull(factory);
        var provider = factory(services);
        var disposable = Assert.IsAssignableFrom<IAsyncDisposable>(provider);
        await provider.ProbeAsync(cancellationToken);
        await disposable.DisposeAsync();
        Assert.Same(services, received);
    }

    [Fact]
    public async Task AddRespProvider_HostLoggerFactory_CreatesTheProviderLoggersThroughIt()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        builder
            .Setup(x => x.AddProvider(It.IsAny<Func<IServiceProvider, ICoordinationProvider>>()))
            .Callback<Func<IServiceProvider, ICoordinationProvider>>(f => factory = f)
            .Returns(builder.Object)
            .Verifiable();
        List<string> categories = [];
        var loggerFactory = Mocks.Create<ILoggerFactory>();
        loggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Callback<string>(categories.Add)
            .Returns(NullLogger.Instance);
        var collection = new ServiceCollection();
        collection.AddSingleton(loggerFactory.Object);
        await using var services = collection.BuildServiceProvider();

        builder.Object.AddRespProvider(options => options.Configuration = "localhost:1");

        Assert.NotNull(factory);
        var provider = factory(services);
        await using var disposable = Assert.IsAssignableFrom<IAsyncDisposable>(provider);
        var category = typeof(RespRecordWatchService).FullName;
        Assert.Contains(category, categories);
    }

    #endregion
}
