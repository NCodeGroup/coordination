// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgSourceTests
{
    private const string BaseConnectionString =
        "Host=localhost;Database=coordination;Username=u;Password=p";

    #region FromConnectionString Tests

    [Fact]
    public async Task FromConnectionString_Always_TurnsEnlistOff()
    {
        await using var source = PgSource.FromConnectionString(
            BaseConnectionString + ";Enlist=true"
        );

        var builder = new NpgsqlConnectionStringBuilder(source.ConnectionString);

        Assert.False(builder.Enlist);
    }

    [Fact]
    public async Task FromConnectionString_NoKeepAlive_SetsFifteenSeconds()
    {
        await using var source = PgSource.FromConnectionString(BaseConnectionString);

        var builder = new NpgsqlConnectionStringBuilder(source.ConnectionString);

        Assert.Equal(15, builder.KeepAlive);
    }

    [Fact]
    public async Task FromConnectionString_KeepAliveSet_KeepsIt()
    {
        await using var source = PgSource.FromConnectionString(
            BaseConnectionString + ";Keepalive=30"
        );

        var builder = new NpgsqlConnectionStringBuilder(source.ConnectionString);

        Assert.Equal(30, builder.KeepAlive);
    }

    [Fact]
    public async Task FromConnectionString_SeveralHosts_Builds()
    {
        await using var source = PgSource.FromConnectionString(
            "Host=first.example,second.example;Database=coordination;Username=u;Password=p"
        );

        var builder = new NpgsqlConnectionStringBuilder(source.ConnectionString);

        Assert.Equal("first.example,second.example", builder.Host);
        Assert.False(builder.Enlist);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_Owned_DisposesTheDataSource()
    {
        var source = PgSource.FromConnectionString(BaseConnectionString);

        await source.DisposeAsync();
        await source.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await source.OpenAsync(CancellationToken.None)
        );
    }

    [Fact]
    public async Task DisposeAsync_Shared_LeavesTheDataSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dataSource = NpgsqlDataSource.Create(PgOfflineServer.ConnectionString);
        var source = PgSource.FromDataSource(dataSource);

        await source.DisposeAsync();

        await Assert.ThrowsAsync<NpgsqlException>(async () =>
            await dataSource.OpenConnectionAsync(cancellationToken)
        );
    }

    #endregion
}
