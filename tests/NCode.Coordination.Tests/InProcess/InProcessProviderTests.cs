// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessProviderTests
{
    private static InProcessProvider CreateProvider()
    {
        var time = InProcessTesting.CreateTime();
        return new InProcessProvider(time);
    }

    private static PrimitiveSupport GetSupport(InProcessProvider provider, Primitive primitive) =>
        Assert.Single(provider.Declaration.Primitives, x => x.Primitive == primitive);

    #region Declaration Property Tests

    [Fact]
    public void Declaration_WhenRead_ListsEveryPrimitiveOnce()
    {
        var provider = CreateProvider();

        var declared = provider.Declaration.Primitives.Select(x => x.Primitive);

        Assert.Equal(Primitives.All, declared);
    }

    [Fact]
    public void Declaration_WhenRead_NamesTheProvider()
    {
        var provider = CreateProvider();

        Assert.Equal(
            InProcessCoordinationBuilderExtensions.ProviderName,
            provider.Declaration.Provider
        );
    }

    [Fact]
    public void Declaration_WhenRead_DeclaresTheRecordGuarantees()
    {
        var provider = CreateProvider();

        var support = GetSupport(provider, Primitives.Record);

        Assert.True(support.IsSupported);
        Assert.Equal<Guarantee>(
            [Guarantees.LinearizableWithinProcess, Guarantees.Volatile, Guarantees.ServerClock],
            support.Guarantees
        );
    }

    [Fact]
    public void Declaration_WhenRead_DeclaresAtomicityAcrossPartitionsForTransactionsAndCounters()
    {
        var provider = CreateProvider();

        var transaction = GetSupport(provider, Primitives.Transaction);
        var counter = GetSupport(provider, Primitives.Counter);

        Assert.True(transaction.IsSupported);
        Assert.Contains(Guarantees.AtomicAcrossPartitions, transaction.Guarantees);
        Assert.True(counter.IsSupported);
        Assert.Contains(Guarantees.AtomicAcrossPartitions, counter.Guarantees);
    }

    [Fact]
    public void Declaration_WhenRead_DeclaresSequencesWithoutExpiry()
    {
        var provider = CreateProvider();

        var support = GetSupport(provider, Primitives.Sequence);

        Assert.True(support.IsSupported);
        Assert.Equal<Guarantee>(
            [Guarantees.LinearizableWithinProcess, Guarantees.Volatile],
            support.Guarantees
        );
    }

    [Fact]
    public void Declaration_WhenRead_SupportsEveryPrimitive()
    {
        var provider = CreateProvider();

        Assert.All(provider.Declaration.Primitives, x => Assert.True(x.IsSupported));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("membership")]
    [InlineData("lease")]
    [InlineData("semaphore")]
    [InlineData("election")]
    [InlineData("claim")]
    public void Declaration_WhenRead_DeclaresTheStateGuaranteesForOwnershipAndLiveness(string name)
    {
        var provider = CreateProvider();

        var support = GetSupport(provider, new Primitive(name));

        Assert.Equal<Guarantee>(
            [Guarantees.LinearizableWithinProcess, Guarantees.Volatile, Guarantees.ServerClock],
            support.Guarantees
        );
    }

    [Fact]
    public void Declaration_WhenRead_DeclaresGapDetectionForWatchesAndNotifications()
    {
        var provider = CreateProvider();

        var watch = GetSupport(provider, Primitives.RecordWatch);
        var notification = GetSupport(provider, Primitives.Notification);

        Assert.Equal<Guarantee>(
            [Guarantees.LinearizableWithinProcess, Guarantees.AtMostOnceWithGapDetection],
            watch.Guarantees
        );
        Assert.Equal<Guarantee>([Guarantees.AtMostOnceWithGapDetection], notification.Guarantees);
    }

    #endregion

    #region GetPrimitiveService Tests

    [Theory]
    [InlineData(typeof(ISessionService))]
    [InlineData(typeof(IMembershipService))]
    [InlineData(typeof(ILeaseService))]
    [InlineData(typeof(ISemaphoreService))]
    [InlineData(typeof(IElectionService))]
    [InlineData(typeof(IClaimService))]
    [InlineData(typeof(IRecordService))]
    [InlineData(typeof(ITransactionService))]
    [InlineData(typeof(IRecordWatchService))]
    [InlineData(typeof(IOccurrenceService))]
    [InlineData(typeof(ISetService))]
    [InlineData(typeof(ICounterService))]
    [InlineData(typeof(ISequenceService))]
    [InlineData(typeof(INotificationService))]
    public void GetPrimitiveService_WhenPrimitiveIsImplemented_ReturnsItsService(Type type)
    {
        var provider = CreateProvider();

        var service = provider.GetPrimitiveService(type);

        Assert.NotNull(service);
        Assert.IsAssignableFrom(type, service);
    }

    [Fact]
    public void GetPrimitiveService_WhenTypeIsNotAPrimitive_ReturnsNull()
    {
        var provider = CreateProvider();

        var service = provider.GetPrimitiveService(typeof(string));

        Assert.Null(service);
    }

    [Fact]
    public void GetPrimitiveService_WhenTypeIsNull_Throws()
    {
        var provider = CreateProvider();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            provider.GetPrimitiveService(null!)
        );

        Assert.Equal("serviceType", exception.ParamName);
    }

    #endregion

    #region ProbeAsync Tests

    [Fact]
    public async Task ProbeAsync_WhenCalled_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = CreateProvider();

        await provider.ProbeAsync(cancellationToken);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WhenCalledTwice_Completes()
    {
        var provider = CreateProvider();

        provider.Dispose();
        provider.Dispose();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTimeIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new InProcessProvider(null!));

        Assert.Equal("time", exception.ParamName);
    }

    #endregion
}
