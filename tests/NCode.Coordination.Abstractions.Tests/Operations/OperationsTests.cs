// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class OperationsTests
{
    private static Operation[] All { get; } =
    [
        Operations.SessionOpen,
        Operations.SessionKeepAlive,
        Operations.SessionClose,
        Operations.SessionWaitForEnd,
        Operations.MembershipJoin,
        Operations.MembershipLeave,
        Operations.MembershipRead,
        Operations.MembershipWatch,
        Operations.LeaseTryAcquire,
        Operations.LeaseRenew,
        Operations.LeaseRelease,
        Operations.LeaseWaitForRelease,
        Operations.SemaphoreTryAcquire,
        Operations.SemaphoreRenew,
        Operations.SemaphoreRelease,
        Operations.SemaphoreWaitForRelease,
        Operations.ElectionTryLead,
        Operations.ElectionRenew,
        Operations.ElectionResign,
        Operations.ElectionRead,
        Operations.ElectionWatch,
        Operations.ClaimTryClaim,
        Operations.RecordCreate,
        Operations.RecordRead,
        Operations.RecordCompareAndSwap,
        Operations.RecordCompareAndDelete,
        Operations.RecordTake,
        Operations.RecordList,
        Operations.RecordWatchKey,
        Operations.RecordWatchPrefix,
        Operations.TransactionExecute,
        Operations.OccurrenceTryRecord,
        Operations.SetAdd,
        Operations.SetRemove,
        Operations.SetList,
        Operations.SetClear,
        Operations.CounterIncrement,
        Operations.CounterRead,
        Operations.CounterReset,
        Operations.CounterIncrementBatch,
        Operations.SequenceNext,
        Operations.SequenceReserve,
        Operations.NotificationPublish,
        Operations.NotificationSubscribe,
    ];

    private static OperationDefinition DefinitionOf(Operation operation) =>
        Assert.Single(Operations.Definitions, definition => definition.Operation == operation);

    #region Definitions Property Tests

    [Fact]
    public void Definitions_Always_HoldsEachBuiltInOperationOnceInOrder()
    {
        Operation[] actual = [.. Operations.Definitions.Select(d => d.Operation)];
        var distinct = All.Distinct().Count();

        Assert.Equal(44, All.Length);
        Assert.Equal<Operation>(All, actual);
        Assert.Equal(All.Length, distinct);
    }

    [Fact]
    public void Definitions_Always_CoversEveryPrimitive()
    {
        foreach (var primitive in Primitives.All)
        {
            Assert.Contains(Operations.Definitions, d => d.Primitive == primitive);
        }
    }

    [Fact]
    public void Definitions_Always_UsesTheKnownDimensions()
    {
        string[] dimensions =
        [
            GuaranteeDimensions.Consistency,
            GuaranteeDimensions.Durability,
            GuaranteeDimensions.Expiry,
            GuaranteeDimensions.Delivery,
            GuaranteeDimensions.Atomicity,
        ];

        foreach (var definition in Operations.Definitions)
        {
            Assert.All(definition.Dimensions, dimension => Assert.Contains(dimension, dimensions));
        }
    }

    [Fact]
    public void Definitions_Always_GivesAtomicityOnlyToTransactionsAndCounterBatches()
    {
        Operation[] expected = [Operations.TransactionExecute, Operations.CounterIncrementBatch];

        Operation[] actual =
        [
            .. Operations
                .Definitions.Where(d => d.Dimensions.Contains(GuaranteeDimensions.Atomicity))
                .Select(d => d.Operation),
        ];

        Assert.Equal<Operation>(expected, actual);
    }

    [Fact]
    public void Definitions_WhenNotificationPublish_CarriesOnlyDelivery()
    {
        var definition = DefinitionOf(Operations.NotificationPublish);

        Assert.Equal<string>([GuaranteeDimensions.Delivery], definition.Dimensions);
    }

    [Fact]
    public void Definitions_WhenRecordRead_CarriesOnlyConsistency()
    {
        var definition = DefinitionOf(Operations.RecordRead);

        Assert.Equal<string>([GuaranteeDimensions.Consistency], definition.Dimensions);
    }

    #endregion
}
