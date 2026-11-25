// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of a counter: its state is the count, which starts at zero.
/// </summary>
internal class CounterModel : ISequentialModel<long>
{
    /// <summary>
    /// Checks that the output is the result of a batch of one increment that brought the count to the sum.
    /// </summary>
    /// <param name="output">The output that the call returned.</param>
    /// <param name="sum">The count after the increment.</param>
    /// <param name="limit">The limit the batch checked the count against.</param>
    /// <returns>True when the output is the result the specification allows.</returns>
    private static bool IsBatchResult(object? output, long sum, long limit)
    {
        if (output is not CounterBatchResult result)
        {
            return false;
        }

        var expectedExceeded = sum > limit ? 0 : (int?)null;
        return result.Counts.Length == 1
            && result.Counts[0] == sum
            && result.ExceededIndex == expectedExceeded;
    }

    /// <inheritdoc />
    public long Initial => 0;

    /// <inheritdoc />
    public bool TryApply(
        long state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out long next
    )
    {
        next = state;

        if (operation.Input is not CounterCall call)
        {
            return false;
        }

        var sum = state + call.Amount;

        switch (call.Operation)
        {
            case CounterCall.Increment:
                next = sum;
                return operation.IsIndeterminate
                    || (operation.Output is long count && count == sum);

            case CounterCall.Batch:
                next = sum;
                return operation.IsIndeterminate
                    || IsBatchResult(operation.Output, sum, call.Limit);

            case CounterCall.Read:
                return operation.IsIndeterminate
                    || (operation.Output is long current && current == state);

            case CounterCall.Reset:
                next = 0;
                return true;

            default:
                return false;
        }
    }
}
