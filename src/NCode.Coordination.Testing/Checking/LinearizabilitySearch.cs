// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// Searches the calls of one key for an order that a sequential specification allows.
/// </summary>
internal static class LinearizabilitySearch
{
    /// <summary>
    /// The number of distinct configurations a search visits before it gives up.
    /// </summary>
    public const int MaxConfigurations = 1_000_000;

    /// <summary>
    /// Decides whether the calls of one key have a linearization under a model.
    /// </summary>
    /// <remarks>
    /// This is Wing and Gong's search over the calls that no other call must precede, with Lowe's cache of the
    /// configurations already visited: a set of linearized calls together with the state they led to.
    /// </remarks>
    /// <typeparam name="TState">The type of the model's state.</typeparam>
    /// <param name="model">The sequential specification of the primitive.</param>
    /// <param name="operations">The calls of one key ordered by <see cref="HistoryOperation.Invoked"/>, none failed.</param>
    /// <param name="maxConfigurations">The number of configurations to visit before returning undecided.</param>
    /// <returns>The verdict for the calls.</returns>
    public static LinearizabilityVerdict Search<TState>(
        ISequentialModel<TState> model,
        IReadOnlyList<HistoryOperation> operations,
        int maxConfigurations
    )
        where TState : notnull
    {
        var count = operations.Count;
        var completed = CountCompleted(operations);
        var seen = new HashSet<(LinearizedSet, TState)>();
        var stack =
            new Stack<(LinearizedSet Linearized, int Completed, TState State, int Cursor)>();
        var empty = LinearizedSet.Empty(count);
        stack.Push((empty, 0, model.Initial, 0));

        while (stack.TryPop(out var frame))
        {
            if (frame.Completed == completed)
            {
                return LinearizabilityVerdict.Linearizable;
            }

            var limit = LeastPendingResponse(operations, frame.Linearized);

            for (
                var index = frame.Cursor;
                index < count && operations[index].Invoked < limit;
                index++
            )
            {
                var operation = operations[index];
                if (
                    frame.Linearized.Contains(index)
                    || !model.TryApply(frame.State, operation, out var next)
                )
                {
                    continue;
                }

                var linearized = frame.Linearized.Add(index);
                if (!seen.Add((linearized, next)))
                {
                    continue;
                }

                if (seen.Count > maxConfigurations)
                {
                    return LinearizabilityVerdict.Undecided;
                }

                var advanced = frame.Completed + (operation.IsIndeterminate ? 0 : 1);
                stack.Push((frame.Linearized, frame.Completed, frame.State, index + 1));
                stack.Push((linearized, advanced, next, 0));
                break;
            }
        }

        return LinearizabilityVerdict.NotLinearizable;
    }

    /// <summary>
    /// Counts the calls that returned, which a linearization must include.
    /// </summary>
    /// <param name="operations">The calls of one key.</param>
    /// <returns>The number of calls that are not indeterminate.</returns>
    private static int CountCompleted(IReadOnlyList<HistoryOperation> operations)
    {
        var completed = 0;

        foreach (var operation in operations)
        {
            if (!operation.IsIndeterminate)
            {
                completed++;
            }
        }

        return completed;
    }

    /// <summary>
    /// Finds the earliest response among the returned calls not yet linearized: a call invoked after it cannot come
    /// next, because that call must precede it.
    /// </summary>
    /// <param name="operations">The calls of one key.</param>
    /// <param name="linearized">The calls already linearized.</param>
    /// <returns>The least response stamp, or <see cref="long.MaxValue"/> when no returned call is pending.</returns>
    private static long LeastPendingResponse(
        IReadOnlyList<HistoryOperation> operations,
        LinearizedSet linearized
    )
    {
        var limit = long.MaxValue;

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            if (
                !linearized.Contains(index)
                && !operation.IsIndeterminate
                && operation.Responded < limit
            )
            {
                limit = operation.Responded;
            }
        }

        return limit;
    }
}
