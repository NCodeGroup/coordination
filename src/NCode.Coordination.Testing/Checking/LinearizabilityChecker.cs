// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// Checks a whole history for linearizability, one key at a time, and cuts a failure to its shortest failing prefix.
/// </summary>
internal static class LinearizabilityChecker
{
    /// <summary>
    /// Checks a history against a model, giving up after <see cref="LinearizabilitySearch.MaxConfigurations"/>
    /// configurations for a key.
    /// </summary>
    /// <typeparam name="TState">The type of the model's state.</typeparam>
    /// <param name="model">The sequential specification of the primitive.</param>
    /// <param name="history">The calls of the run.</param>
    /// <returns>The first key whose calls are not linearizable, or a result that says every key is.</returns>
    public static LinearizabilityResult Check<TState>(
        ISequentialModel<TState> model,
        IReadOnlyList<HistoryOperation> history
    )
        where TState : notnull => Check(model, history, LinearizabilitySearch.MaxConfigurations);

    /// <summary>
    /// Checks a history against a model.
    /// </summary>
    /// <remarks>
    /// A call that failed took no effect and is left out. Each key is checked on its own, because a history is
    /// linearizable exactly when the history of each key is: the calls of <see cref="HistoryOperation.EveryPartition"/>
    /// join every key. The keys are checked in ordinal order and the first that fails is reported.
    /// </remarks>
    /// <typeparam name="TState">The type of the model's state.</typeparam>
    /// <param name="model">The sequential specification of the primitive.</param>
    /// <param name="history">The calls of the run.</param>
    /// <param name="maxConfigurations">The number of configurations to visit for a key before it is undecided.</param>
    /// <returns>The first key whose calls are not linearizable, or a result that says every key is.</returns>
    public static LinearizabilityResult Check<TState>(
        ISequentialModel<TState> model,
        IReadOnlyList<HistoryOperation> history,
        int maxConfigurations
    )
        where TState : notnull
    {
        var calls = history.Where(operation => operation.Outcome != HistoryOutcome.Failed).ToList();
        var partitions = calls
            .Where(operation => operation.Partition != HistoryOperation.EveryPartition)
            .Select(operation => operation.Partition)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (var partition in partitions)
        {
            var operations = CallsOf(calls, partition);
            var verdict = LinearizabilitySearch.Search(model, operations, maxConfigurations);

            if (verdict == LinearizabilityVerdict.Linearizable)
            {
                continue;
            }

            var shown =
                verdict == LinearizabilityVerdict.NotLinearizable
                    ? ShortestFailingPrefix(model, operations, maxConfigurations)
                    : operations;

            return new LinearizabilityResult(verdict, partition, shown);
        }

        return LinearizabilityResult.Linearizable;
    }

    /// <summary>
    /// Cuts a history at a point: the calls made before it, with a call still in flight at the cut left indeterminate.
    /// </summary>
    /// <param name="operations">The calls of one key.</param>
    /// <param name="cut">The stamp on the history's clock at which to cut.</param>
    /// <returns>The calls invoked before the cut.</returns>
    internal static ImmutableArray<HistoryOperation> Prefix(
        IReadOnlyList<HistoryOperation> operations,
        long cut
    )
    {
        var prefix = ImmutableArray.CreateBuilder<HistoryOperation>();

        foreach (var operation in operations)
        {
            if (operation.Invoked >= cut)
            {
                continue;
            }

            prefix.Add(
                operation.Responded > cut
                    ? operation with
                    {
                        Responded = long.MaxValue,
                        Outcome = HistoryOutcome.Indeterminate,
                    }
                    : operation
            );
        }

        return prefix.ToImmutable();
    }

    /// <summary>
    /// Collects the calls of one key, with those of every key, ordered by when they were invoked.
    /// </summary>
    /// <param name="calls">The calls of the run that did not fail.</param>
    /// <param name="partition">The key.</param>
    /// <returns>The calls of the key.</returns>
    private static ImmutableArray<HistoryOperation> CallsOf(
        IReadOnlyList<HistoryOperation> calls,
        string partition
    ) =>
        [
            .. calls
                .Where(operation =>
                    operation.Partition == HistoryOperation.EveryPartition
                    || string.Equals(operation.Partition, partition, StringComparison.Ordinal)
                )
                .OrderBy(operation => operation.Invoked),
        ];

    /// <summary>
    /// Finds the shortest prefix of a failing history that still fails.
    /// </summary>
    /// <remarks>
    /// Every prefix of a linearizable history is linearizable, so the prefixes that fail are the longer ones and a
    /// binary search over the points where a call returned finds the shortest. An undecided prefix counts as passing.
    /// </remarks>
    /// <typeparam name="TState">The type of the model's state.</typeparam>
    /// <param name="model">The sequential specification of the primitive.</param>
    /// <param name="operations">The calls of one key, which are not linearizable.</param>
    /// <param name="maxConfigurations">The number of configurations to visit for a prefix before it is undecided.</param>
    /// <returns>The shortest prefix that is not linearizable.</returns>
    private static ImmutableArray<HistoryOperation> ShortestFailingPrefix<TState>(
        ISequentialModel<TState> model,
        ImmutableArray<HistoryOperation> operations,
        int maxConfigurations
    )
        where TState : notnull
    {
        long[] cuts =
        [
            .. operations
                .Where(operation => !operation.IsIndeterminate)
                .Select(operation => operation.Responded)
                .Distinct()
                .Order(),
        ];

        if (cuts.Length == 0)
        {
            return operations;
        }

        var low = 0;
        var high = cuts.Length - 1;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var prefix = Prefix(operations, cuts[middle]);
            var verdict = LinearizabilitySearch.Search(model, prefix, maxConfigurations);

            if (verdict == LinearizabilityVerdict.NotLinearizable)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return Prefix(operations, cuts[low]);
    }
}
