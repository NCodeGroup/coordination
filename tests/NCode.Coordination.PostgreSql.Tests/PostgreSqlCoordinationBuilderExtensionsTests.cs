// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PostgreSqlCoordinationBuilderExtensionsTests : IDisposable
{
    private const string ConnectionString = PgOfflineServer.ConnectionString;

    private static Func<IServiceProvider, ICoordinationProvider> AnyFactory =>
        It.IsAny<Func<IServiceProvider, ICoordinationProvider>>();

    private static void SetDuration(
        PostgreSqlProviderOptions options,
        string option,
        TimeSpan value
    )
    {
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
            case "LogRetention":
                options.LogRetention = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(option), option, "No such option.");
        }
    }

    private static string RangeOf(string option) =>
        option == "LogRetention"
            ? "PostgreSqlProviderOptions.LogRetention is from one second to one day."
            : $"PostgreSqlProviderOptions.{option} is at least one millisecond and less than one hour.";

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
                .Setup(x => x.AddProvider(AnyFactory))
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
        Assert.StartsWith(
            "Set exactly one of PostgreSqlProviderOptions.ConnectionString and PostgreSqlProviderOptions.DataSource.",
            exception.Message
        );
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

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.Object.AddPostgreSqlProvider(options =>
            {
                options.ConnectionString = ConnectionString;
                options.Schema = schema;
            })
        );

        Assert.Equal("configure", exception.ParamName);
        Assert.StartsWith(
            $"'{schema}' is not a schema name the provider takes: use 1 to 63 lowercase letters, digits and underscores, starting with a letter or an underscore.",
            exception.Message
        );
    }

    [Fact]
    public void AddPostgreSqlProvider_ValidSchema_AddsTheProvider()
    {
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        var builder = CreateBuilder(f => factory = f);

        var result = builder.Object.AddPostgreSqlProvider(options =>
        {
            options.ConnectionString = ConnectionString;
            options.Schema = "a_1";
        });

        Assert.Same(builder.Object, result);
        Assert.NotNull(factory);
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
    [InlineData("StatementTimeout", 9_999L)]
    [InlineData("LockTimeout", 9_999L)]
    [InlineData("IdleInTransactionTimeout", 9_999L)]
    [InlineData("SweepInterval", 9_999L)]
    [InlineData("CheckInterval", 9_999L)]
    [InlineData("LogRetention", 9_999_999L)]
    public void AddPostgreSqlProvider_DurationOutOfRange_Throws(string option, long ticks)
    {
        var builder = CreateBuilder();
        var value = TimeSpan.FromTicks(ticks);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.Object.AddPostgreSqlProvider(options =>
            {
                options.ConnectionString = ConnectionString;
                SetDuration(options, option, value);
            })
        );

        var range = RangeOf(option);

        Assert.Equal("configure", exception.ParamName);
        Assert.StartsWith(range, exception.Message);
    }

    [Theory]
    [InlineData("StatementTimeout", 10_000L)]
    [InlineData("LockTimeout", 10_000L)]
    [InlineData("IdleInTransactionTimeout", 10_000L)]
    [InlineData("SweepInterval", 10_000L)]
    [InlineData("CheckInterval", 10_000L)]
    [InlineData("LogRetention", 10_000_000L)]
    [InlineData("StatementTimeout", 35_999_999_999L)]
    [InlineData("LockTimeout", 35_999_999_999L)]
    [InlineData("IdleInTransactionTimeout", 35_999_999_999L)]
    [InlineData("SweepInterval", 35_999_999_999L)]
    [InlineData("CheckInterval", 35_999_999_999L)]
    [InlineData("LogRetention", 864_000_000_000L)]
    public void AddPostgreSqlProvider_DurationAtTheEdge_AddsTheProvider(string option, long ticks)
    {
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        var builder = CreateBuilder(f => factory = f);
        var value = TimeSpan.FromTicks(ticks);

        var result = builder.Object.AddPostgreSqlProvider(options =>
        {
            options.ConnectionString = ConnectionString;
            SetDuration(options, option, value);
        });

        Assert.Same(builder.Object, result);
        Assert.NotNull(factory);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100_000)]
    public void AddPostgreSqlProvider_BatchSizeAtTheEdge_AddsTheProvider(int batchSize)
    {
        Func<IServiceProvider, ICoordinationProvider>? factory = null;
        var builder = CreateBuilder(f => factory = f);

        var result = builder.Object.AddPostgreSqlProvider(options =>
        {
            options.ConnectionString = ConnectionString;
            options.SweepBatchSize = batchSize;
        });

        Assert.Same(builder.Object, result);
        Assert.NotNull(factory);
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
