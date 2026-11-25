// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of two counters that one partition holds and of batches over both. A batch applies
/// its increments in order and stops after the first count over its limit, which stays applied.
/// </summary>
internal class CounterBatchModel() : PairModel<long>(new CounterModel())
{
    /// <summary>
    /// Checks that a batch result holds exactly the expected counts and exceeded index.
    /// </summary>
    /// <param name="result">The result that the call returned.</param>
    /// <param name="counts">The counts that the specification allows.</param>
    /// <param name="exceededIndex">The exceeded index that the specification allows.</param>
    /// <returns>True when the result is the one the specification allows.</returns>
    private static bool IsCounts(CounterBatchResult result, long[] counts, int? exceededIndex) =>
        result.Counts.AsSpan().SequenceEqual(counts) && result.ExceededIndex == exceededIndex;

    /// <inheritdoc />
    protected override bool TryApplySpanning(
        PairState<long> state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out PairState<long> next
    )
    {
        next = state;

        if (operation.Input is not CounterBatchCall call)
        {
            return false;
        }

        var first = state.First + call.FirstAmount;
        var stops = first > call.Limit;
        var second = stops ? state.Second : state.Second + call.SecondAmount;
        next = new PairState<long>(first, second);

        if (operation.IsIndeterminate)
        {
            return true;
        }

        if (operation.Output is not CounterBatchResult result)
        {
            return false;
        }

        if (stops)
        {
            return IsCounts(result, [first], 0);
        }

        int? exceededIndex = second > call.Limit ? 1 : null;
        return IsCounts(result, [first, second], exceededIndex);
    }
}
