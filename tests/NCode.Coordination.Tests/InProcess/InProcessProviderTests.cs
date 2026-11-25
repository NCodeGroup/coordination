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

    [Theory]
    [InlineData("session")]
    [InlineData("membership")]
    [InlineData("lease")]
    [InlineData("semaphore")]
    [InlineData("election")]
    [InlineData("claim")]
    [InlineData("record-watch")]
    [InlineData("notification")]
    public void Declaration_WhenPrimitiveIsNotImplemented_IsUnsupported(string name)
    {
        var provider = CreateProvider();

        var primitive = new Primitive(name);

        var support = GetSupport(provider, primitive);

        Assert.False(support.IsSupported);
        Assert.Equal(InProcessProvider.UnsupportedReason, support.UnsupportedReason);
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
    public void GetPrimitiveService_WhenPrimitiveIsImplemented_ReturnsItsService(Type type)
    {
        var provider = CreateProvider();

        var service = provider.GetPrimitiveService(type);

        Assert.NotNull(service);
        Assert.IsAssignableFrom(type, service);
    }

    [Fact]
    public void GetPrimitiveService_WhenPrimitiveIsNotImplemented_ReturnsNull()
    {
        var provider = CreateProvider();

        var service = provider.GetPrimitiveService(typeof(ILeaseService));

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

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTimeIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new InProcessProvider(null!));

        Assert.Equal("time", exception.ParamName);
    }

    #endregion
}
