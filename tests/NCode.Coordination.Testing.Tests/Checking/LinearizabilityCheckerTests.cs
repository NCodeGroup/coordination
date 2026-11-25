// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Checking.Tests;

public sealed class LinearizabilityCheckerTests
{
    private static RegisterModel Model { get; } = new();

    #region Check Tests

    [Fact]
    public void Check_WhenHistoryIsSequentiallyLegal_ReturnsLinearizable()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 2, null),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 3, 4, 1),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.Linearizable, result.Verdict);
        Assert.Null(result.Partition);
        Assert.Empty(result.Operations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Check_WhenReadOverlapsWrite_AcceptsEitherValue(int value)
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 4, null),
            HistoryTesting.Operation(1, "p", RegisterModel.Read, 2, 3, value),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.Linearizable, result.Verdict);
    }

    [Fact]
    public void Check_WhenReadIsStaleAfterCompletedWrite_ReturnsNotLinearizableWithItsKey()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 2, null),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 3, 4, 0),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.NotLinearizable, result.Verdict);
        Assert.Equal("p", result.Partition);
        Assert.Equal<HistoryOperation>(history, result.Operations);
    }

    [Fact]
    public void Check_WhenOnlyOneKeyIsStale_NamesThatKey()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "a", 1, 1, 2, null),
            HistoryTesting.Operation(0, "a", RegisterModel.Read, 3, 4, 1),
            HistoryTesting.Operation(0, "b", 1, 5, 6, null),
            HistoryTesting.Operation(0, "b", RegisterModel.Read, 7, 8, 0),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.NotLinearizable, result.Verdict);
        Assert.Equal("b", result.Partition);
    }

    [Fact]
    public void Check_WhenSeveralKeysAreStale_NamesTheFirstInOrdinalOrder()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "b", 1, 1, 2, null),
            HistoryTesting.Operation(0, "b", RegisterModel.Read, 3, 4, 0),
            HistoryTesting.Operation(0, "a", 1, 5, 6, null),
            HistoryTesting.Operation(0, "a", RegisterModel.Read, 7, 8, 0),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.NotLinearizable, result.Verdict);
        Assert.Equal("a", result.Partition);
    }

    [Fact]
    public void Check_WhenIndeterminateWriteIsSeenLater_ReturnsLinearizable()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Indeterminate(5),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 2, 3, 5),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.Linearizable, result.Verdict);
    }

    [Fact]
    public void Check_WhenIndeterminateWriteIsNeverSeen_ReturnsLinearizable()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Indeterminate(5),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 2, 3, 0),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.Linearizable, result.Verdict);
    }

    [Fact]
    public void Check_WhenIndeterminateWriteIsCalledAfterTheReadReturned_ReturnsNotLinearizable()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 1, 2, 5),
            HistoryTesting.Indeterminate(5) with
            {
                Invoked = 3,
            },
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.NotLinearizable, result.Verdict);
        Assert.Equal("p", result.Partition);
    }

    [Fact]
    public void Check_WhenWriteReturnedAndIsReadLater_ReturnsLinearizable()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Returned(5, null),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 3, 4, 5),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.Linearizable, result.Verdict);
    }

    [Fact]
    public void Check_WhenFailedWriteIsReadLater_ReturnsNotLinearizable()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Returned(5, null) with
            {
                Outcome = HistoryOutcome.Failed,
            },
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 3, 4, 5),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.NotLinearizable, result.Verdict);
        Assert.Equal("p", result.Partition);
    }

    [Fact]
    public void Check_WhenCallsAreStale_CutsThemToTheShortestFailingPrefix()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 2, null),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 3, 4, 1),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 5, 6, 0),
            HistoryTesting.Operation(0, "p", 2, 7, 8, null),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 9, 10, 2),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.NotLinearizable, result.Verdict);
        Assert.Equal<HistoryOperation>(history[..3], result.Operations);
    }

    [Fact]
    public void Check_WhenEveryPartitionCallIsMade_CountsItInEachKey()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(
                HistoryOperation.TestClient,
                HistoryOperation.EveryPartition,
                7,
                1,
                2,
                null
            ),
            HistoryTesting.Operation(0, "a", RegisterModel.Read, 3, 4, 7),
            HistoryTesting.Operation(0, "b", RegisterModel.Read, 5, 6, 7),
        ];

        var result = LinearizabilityChecker.Check(Model, history);

        Assert.Equal(LinearizabilityVerdict.Linearizable, result.Verdict);
    }

    [Fact]
    public void Check_WhenMaxConfigurationsIsReached_ReturnsUndecidedWithAllItsCalls()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 2, null),
            HistoryTesting.Operation(0, "p", 2, 3, 4, null),
        ];

        var result = LinearizabilityChecker.Check(Model, history, maxConfigurations: 1);

        Assert.Equal(LinearizabilityVerdict.Undecided, result.Verdict);
        Assert.Equal("p", result.Partition);
        Assert.Equal<HistoryOperation>(history, result.Operations);
    }

    #endregion

    #region Prefix Tests

    [Fact]
    public void Prefix_WhenCallsAreInvokedAfterTheCut_LeavesThemOut()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 2, null),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 3, 4, 1),
            HistoryTesting.Operation(0, "p", 2, 5, 6, null),
        ];

        var prefix = LinearizabilityChecker.Prefix(history, 4);

        Assert.Equal<HistoryOperation>(history[..2], prefix);
    }

    [Fact]
    public void Prefix_WhenCallIsInFlightAtTheCut_LeavesItIndeterminate()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 5, null),
            HistoryTesting.Operation(1, "p", RegisterModel.Read, 2, 3, 0),
        ];

        var prefix = LinearizabilityChecker.Prefix(history, 3);

        Assert.Equal(2, prefix.Length);
        Assert.True(prefix[0].IsIndeterminate);
        Assert.Equal(long.MaxValue, prefix[0].Responded);
        Assert.Equal(history[1], prefix[1]);
    }

    #endregion
}
