// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PostgreSqlCoordinationBuilderExtensionsTests : IDisposable
{
    private const string ConnectionString = "Host=127.0.0.1;Port=1;Database=coordination";

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private Mock<ICoordinationBuilder> CreateBuilder(
        Action<Func<IServiceProvider, ICoordinationProvider>>? capture = null
    )
    {
        var builder = Mocks.Create<ICoordinationBuilder>();
        if (capture is not null)
        {
            builder
                .Setup(x =>
                    x.AddProvider(It.IsAny<Func<IServiceProvider, ICoordinationProvider>>())
                )
                .Callback(capture)
                .Returns(builder.Object)
                .Verifiable();
        }

        return builder;
    }

    #region AddPostgreSqlProvider Tests

    [Fact]
    public void AddPostgreSqlProvider_NeitherSource_Throws()
    {
        var builder = CreateBuilder();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Object.AddPostgreSqlProvider(_ => { })
        );

        Assert.Equal("configure", exception.ParamName);
        Assert.StartsWith(
            "Set exactly one of PostgreSqlProviderOptions.ConnectionString and PostgreSqlProviderOptions.DataSource.",
            exception.Message
        );
    }

    [Fact]
    public void AddPostgreSqlProvider_BothSources_Throws()
    {
        var builder = CreateBuilder();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Object.AddPostgreSqlProvider(options =>
            {
                options.ConnectionString = ConnectionString;
                options.DataSource = _ => throw new InvalidOperationException();
            })
        );

        Assert.Equal("configure", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Coordination")]
    [InlineData("1coordination")]
    [InlineData("co-ordination")]
    [InlineData("a123456789012345678901234567890123456789012345678901234567890123")]
    public void AddPostgreSqlProvider_InvalidSchema_Throws(string schema)
    {
        var builder = CreateBuilder();

        Assert.Throws<ArgumentException>(() =>
            builder.Object.AddPostgreSqlProvider(options =>
            {
                options.ConnectionString = ConnectionString;
                options.Schema = schema;
            })
        );
    }

    [Theory]
    [InlineData("StatementTimeout", 0L)]
    [InlineData("LockTimeout", 0L)]
    [InlineData("IdleInTransactionTimeout", 0L)]
    [InlineData("SweepInterval", 0L)]
    [InlineData("CheckInterval", 0L)]
    [InlineData("LogRetention", 0L)]
    [InlineData("StatementTimeout", 36_000_000_000L)]
    [InlineData("LockTimeout", 36_000_000_000L)]
    [InlineData("IdleInTransactionTimeout", 36_000_000_000L)]
    [InlineData("SweepInterval", 36_000_000_000L)]
    [InlineData("CheckInterval", 36_000_000_000L)]
    [InlineData("LogRetention", 864_000_000_001L)]
    public void AddPostgreSqlProvider_DurationOutOfRange_Throws(string option, long ticks)
    {
        var builder = CreateBuilder();
        var value = TimeSpan.FromTicks(ticks);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.Object.AddPostgreSqlProvider(options =>
            {
                options.ConnectionString = ConnectionString;
                switch (option)
                {
                    case "StatementTimeout":
                        options.StatementTimeout = value;
                        break;
                    case "LockTimeout":
                        options.LockTimeout = value;
                        break;
                    case "IdleInTransactionTimeout":
                        options.IdleInTransactionTimeout = value;
                        break;
                    case "SweepInterval":
                        options.SweepInterval = value;
                        break;
                    case "CheckInterval":
                        options.CheckInterval = value;
                        break;
                    default:
                        options.LogRetention = value;
                        break;
                }
            })
        );

        Assert.Equal("configure", exception.ParamName);
        Assert.Contains(option, exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void AddPostgreSqlProvider_BatchSizeOutOfRange_Throws(int batchSize)
    {
        var builder = CreateBuilder();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.Object.AddPostgreSqlProvider(options =>
            {
                options.ConnectionString = ConnectionString;
                options.SweepBatchSize = batchSize;
            })
        );

        Assert.Equal("configure", exception.ParamName);
        Assert.Contains("SweepBatchSize", exception.Message);
    }

    [Fact]
    public async Task AddPostgreSqlProvider_ConnectionString_AddsProviderWithProfile()
    {
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        var builder = CreateBuilder(f => factory = f);
        await using var services = new ServiceCollection().BuildServiceProvider();

        var result = builder.Object.AddPostgreSqlProvider(options =>
            options.ConnectionString = ConnectionString
        );

        Assert.Same(builder.Object, result);
        Assert.NotNull(factory);
        var provider = factory(services);
        await using var disposable = Assert.IsAssignableFrom<IAsyncDisposable>(provider);
        Assert.Equal("postgresql", provider.Declaration.Provider);
        Assert.Equal("postgresql-asynchronous", provider.Declaration.Profile);
    }

    [Fact]
    public async Task AddPostgreSqlProvider_DataSource_UsesItWithoutDisposingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        var builder = CreateBuilder(f => factory = f);
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        IServiceProvider? received = null;

        builder.Object.AddPostgreSqlProvider(options =>
        {
            options.Profile = PostgreSqlProfiles.Synchronous;
            options.DataSource = s =>
            {
                received = s;
                return dataSource;
            };
        });

        Assert.NotNull(factory);
        var provider = factory(services);
        var disposable = Assert.IsAssignableFrom<IAsyncDisposable>(provider);
        await disposable.DisposeAsync();
        Assert.Same(services, received);
        Assert.Equal("postgresql-synchronous", provider.Declaration.Profile);
        await Assert.ThrowsAsync<NpgsqlException>(async () =>
            await dataSource.OpenConnectionAsync(cancellationToken)
        );
    }

    #endregion
}
