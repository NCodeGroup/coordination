// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;
using Xunit.Sdk;

namespace NCode.Coordination.Testing.Checking.Tests;

public sealed class HistoryAssertTests
{
    private static RegisterModel Model { get; } = new();

    #region Linearizable Tests

    [Fact]
    public void Linearizable_WhenHistoryIsLegal_Passes()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "p", 1, 1, 2, null),
            HistoryTesting.Operation(0, "p", RegisterModel.Read, 3, 4, 1),
        ];

        HistoryAssert.Linearizable(Model, history);
    }

    [Fact]
    public void Linearizable_WhenHistoryIsIllegal_FailsNamingPartitionAndCalls()
    {
        HistoryOperation[] history =
        [
            HistoryTesting.Operation(0, "key-a", 1, 1, 2, null),
            HistoryTesting.Operation(1, "key-a", RegisterModel.Read, 3, 4, 0),
        ];

        var exception = Assert.Throws<FailException>(() =>
            HistoryAssert.Linearizable(Model, history)
        );

        Assert.Contains("'key-a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            HistoryFormat.Describe(history[0]),
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            HistoryFormat.Describe(history[1]),
            exception.Message,
            StringComparison.Ordinal
        );
    }

    #endregion

    #region Describe Tests

    [Fact]
    public void Describe_WhenNotLinearizable_ListsPartitionAndEachCall()
    {
        var first = HistoryTesting.Operation(0, "k", 1, 1, 2, null);
        var second = HistoryTesting.Operation(1, "k", RegisterModel.Read, 3, 4, 0);
        var result = new LinearizabilityResult(
            LinearizabilityVerdict.NotLinearizable,
            "k",
            [first, second]
        );

        var text = HistoryAssert.Describe(result);

        Assert.Equal(
            "The history of partition 'k' is not linearizable. Its shortest failing prefix, in the order of invocation:"
                + "\n  "
                + HistoryFormat.Describe(first)
                + "\n  "
                + HistoryFormat.Describe(second),
            text
        );
    }

    [Fact]
    public void Describe_WhenUndecided_NamesPartitionAndLimit()
    {
        var result = new LinearizabilityResult(LinearizabilityVerdict.Undecided, "k", []);

        var text = HistoryAssert.Describe(result);

        Assert.Equal(
            "The checker gave up on the history of partition 'k' after 1000000 configurations.",
            text
        );
    }

    [Fact]
    public void Describe_WhenLinearizable_SaysSo()
    {
        var text = HistoryAssert.Describe(LinearizabilityResult.Linearizable);

        Assert.Equal("The history is linearizable.", text);
    }

    #endregion
}
