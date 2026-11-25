// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Testing.Histories;
using Xunit;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// Asserts that a history is linearizable and reports the calls that show a failure.
/// </summary>
internal static class HistoryAssert
{
    /// <summary>
    /// Fails the test unless every key of the history has a linearization.
    /// </summary>
    /// <typeparam name="TState">The type of the model's state.</typeparam>
    /// <param name="model">The sequential specification of the primitive.</param>
    /// <param name="history">The calls of the run.</param>
    public static void Linearizable<TState>(
        ISequentialModel<TState> model,
        IReadOnlyList<HistoryOperation> history
    )
        where TState : notnull
    {
        var result = LinearizabilityChecker.Check(model, history);
        if (result.Verdict != LinearizabilityVerdict.Linearizable)
        {
            Assert.Fail(Describe(result));
        }
    }

    /// <summary>
    /// Describes the outcome of a check as text, for the report of a failed test.
    /// </summary>
    /// <param name="result">The outcome of the check.</param>
    /// <returns>The description: the shortest failing prefix, the limit that was reached, or that the history is linearizable.</returns>
    public static string Describe(LinearizabilityResult result) =>
        result.Verdict switch
        {
            LinearizabilityVerdict.NotLinearizable =>
                $"The history of partition '{result.Partition}' is not linearizable. "
                    + "Its shortest failing prefix, in the order of invocation:"
                    + string.Concat(
                        result.Operations.Select(operation =>
                            "\n  " + HistoryFormat.Describe(operation)
                        )
                    ),
            LinearizabilityVerdict.Undecided => string.Create(
                CultureInfo.InvariantCulture,
                $"The checker gave up on the history of partition '{result.Partition}' after {LinearizabilitySearch.MaxConfigurations} configurations."
            ),
            _ => "The history is linearizable.",
        };
}
