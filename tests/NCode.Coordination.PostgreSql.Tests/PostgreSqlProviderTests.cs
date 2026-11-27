// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PostgreSqlProviderTests
{
    private static PostgreSqlProvider CreateProvider(string connectionString) =>
        new(
            PgSource.FromConnectionString(connectionString),
            PostgreSqlProfiles.Asynchronous,
            PgSettings.Default
        );

    #region Declaration Property Tests

    [Fact]
    public async Task Declaration_Always_NamesThePostgreSqlProviderAndProfile()
    {
        await using var provider = CreateProvider("Host=127.0.0.1;Port=1");

        var declaration = provider.Declaration;

        Assert.Equal("postgresql", declaration.Provider);
        Assert.Equal("postgresql-asynchronous", declaration.Profile);
    }

    #endregion

    #region GetPrimitiveService Tests

    [Fact]
    public async Task GetPrimitiveService_AnyType_ReturnsNull()
    {
        await using var provider = CreateProvider("Host=127.0.0.1;Port=1");

        var service = provider.GetPrimitiveService(typeof(IRecordService));

        Assert.Null(service);
    }

    #endregion

    #region ProbeAsync Tests

    [Fact]
    public async Task ProbeAsync_MultiplexingDataSource_ThrowsUnsupported()
    {
        await using var provider = CreateProvider("Host=127.0.0.1;Port=1;Multiplexing=true");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
