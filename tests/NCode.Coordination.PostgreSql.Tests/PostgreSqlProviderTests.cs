// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PostgreSqlProviderTests
{
    private const string UnreachableConnectionString = "Host=127.0.0.1;Port=1";

    public static TheoryData<PostgreSqlProfile> Profiles =>
        [PostgreSqlProfiles.Standalone, PostgreSqlProfiles.Asynchronous];

    private static PostgreSqlProvider CreateProvider(string connectionString)
    {
        var source = PgSource.FromConnectionString(connectionString);

        return new PostgreSqlProvider(source, PostgreSqlProfiles.Asynchronous, PgSettings.Default);
    }

    #region Declaration Property Tests

    [Fact]
    public async Task Declaration_Always_NamesThePostgreSqlProviderAndProfile()
    {
        await using var provider = CreateProvider(UnreachableConnectionString);

        var declaration = provider.Declaration;

        Assert.Equal("postgresql", declaration.Provider);
        Assert.Equal("postgresql-asynchronous", declaration.Profile);
    }

    #endregion

    #region Declare Tests

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Declare_Profile_ReturnsEveryServedPrimitive(PostgreSqlProfile profile)
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
            },
            support =>
            {
                Assert.Equal(Primitives.Session, support.Primitive);
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
                Assert.Equal(Primitives.Membership, support.Primitive);
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
                Assert.Equal(Primitives.Lease, support.Primitive);
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
                Assert.Equal(Primitives.Semaphore, support.Primitive);
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
                Assert.Equal(Primitives.Election, support.Primitive);
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
                Assert.Equal(Primitives.Claim, support.Primitive);
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
                Assert.Equal(Primitives.RecordWatch, support.Primitive);
                Assert.Equal<Guarantee>(
                    [Guarantees.LinearizableUntilFailover, Guarantees.AtMostOnceWithGapDetection],
                    support.Guarantees
                );
            },
            support =>
            {
                Assert.Equal(Primitives.Notification, support.Primitive);
                Assert.Equal<Guarantee>(
                    [Guarantees.AtMostOnceWithGapDetection],
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
    [InlineData(typeof(ISessionService))]
    [InlineData(typeof(IMembershipService))]
    [InlineData(typeof(ILeaseService))]
    [InlineData(typeof(ISemaphoreService))]
    [InlineData(typeof(IElectionService))]
    [InlineData(typeof(IClaimService))]
    [InlineData(typeof(IRecordWatchService))]
    [InlineData(typeof(INotificationService))]
    public async Task GetPrimitiveService_ServedPrimitive_ReturnsItsService(Type serviceType)
    {
        await using var provider = CreateProvider(UnreachableConnectionString);

        var service = provider.GetPrimitiveService(serviceType);

        Assert.NotNull(service);
        Assert.IsAssignableFrom(serviceType, service);
    }

    [Fact]
    public async Task GetPrimitiveService_UnservedPrimitive_ReturnsNull()
    {
        await using var provider = CreateProvider(UnreachableConnectionString);

        var service = provider.GetPrimitiveService(typeof(IDisposable));

        Assert.Null(service);
    }

    #endregion

    #region ProbeAsync Tests

    [Fact]
    public async Task ProbeAsync_MultiplexingDataSource_ThrowsUnsupported()
    {
        const string connectionString = UnreachableConnectionString + ";Multiplexing=true";
        await using var provider = CreateProvider(connectionString);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
