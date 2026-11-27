// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PostgreSqlProviderTests
{
    public static TheoryData<PostgreSqlProfile> Profiles =>
        [PostgreSqlProfiles.Standalone, PostgreSqlProfiles.Asynchronous];

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

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Declaration_Always_DeclaresRecordsTransactionsOccurrencesSetsCountersAndSequences(
        PostgreSqlProfile profile
    )
    {
        var primitives = PostgreSqlProvider.Declare(profile);

        Assert.Collection(
            primitives,
            support =>
            {
                Assert.Equal(Primitives.Record, support.Primitive);
                Assert.Equal<Guarantee>(
                    [
                        Guarantees.LinearizableUntilFailover,
                        profile.Durability,
                        Guarantees.ServerClock,
                    ],
                    support.Guarantees
                );
            },
            support =>
            {
                Assert.Equal(Primitives.Transaction, support.Primitive);
                Assert.Equal<Guarantee>(
                    [
                        Guarantees.LinearizableUntilFailover,
                        profile.Durability,
                        Guarantees.ServerClock,
                        Guarantees.AtomicAcrossPartitions,
                    ],
                    support.Guarantees
                );
            },
            support =>
            {
                Assert.Equal(Primitives.Occurrence, support.Primitive);
                Assert.Equal<Guarantee>(
                    [
                        Guarantees.LinearizableUntilFailover,
                        profile.Durability,
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
                        profile.Durability,
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
                        profile.Durability,
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
                    [Guarantees.LinearizableUntilFailover, profile.Durability],
                    support.Guarantees
                );
            }
        );
    }

    #endregion

    #region GetPrimitiveService Tests

    [Theory]
    [InlineData(typeof(IRecordService))]
    [InlineData(typeof(ITransactionService))]
    [InlineData(typeof(IOccurrenceService))]
    [InlineData(typeof(ISetService))]
    [InlineData(typeof(ICounterService))]
    [InlineData(typeof(ISequenceService))]
    public async Task GetPrimitiveService_ServedPrimitive_ReturnsItsService(Type serviceType)
    {
        await using var provider = CreateProvider("Host=127.0.0.1;Port=1");

        var service = provider.GetPrimitiveService(serviceType);

        Assert.NotNull(service);
        Assert.IsAssignableFrom(serviceType, service);
    }

    [Fact]
    public async Task GetPrimitiveService_UnservedPrimitive_ReturnsNull()
    {
        await using var provider = CreateProvider("Host=127.0.0.1;Port=1");

        var service = provider.GetPrimitiveService(typeof(IRecordWatchService));

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
