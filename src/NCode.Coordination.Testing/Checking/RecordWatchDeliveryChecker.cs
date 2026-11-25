// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Globalization;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// Checks what a record watch received against what the history wrote and deleted and against the delivery that a
/// provider declares.
/// </summary>
internal static class RecordWatchDeliveryChecker
{
    /// <summary>
    /// Checks the changes that a record watch received.
    /// </summary>
    /// <param name="history">The calls of the run, among which the writes and deletions are the <see cref="RecordCall"/> calls.</param>
    /// <param name="changes">What the watch received, in the order it received it.</param>
    /// <param name="closing">The revision that the last write of each key was written at.</param>
    /// <param name="gapDetection">
    /// Whether the provider declares gap detection, which requires the last write of each key or a resync after it.
    /// </param>
    /// <returns>A description of each violation found, empty when the changes are as declared.</returns>
    /// <exception cref="ArgumentNullException">The history or the closing revisions are null.</exception>
    public static ImmutableArray<string> Check(
        IReadOnlyList<HistoryOperation> history,
        ImmutableArray<Delivery<RecordChange>> changes,
        IReadOnlyDictionary<CoordinationKey, Revision> closing,
        bool gapDetection
    )
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(closing);

        var violations = ImmutableArray.CreateBuilder<string>();
        var floor = 0L;

        foreach (var delivery in changes)
        {
            var change = delivery.Item;
            var revision = change.Revision.Value;

            if (change.IsResync)
            {
                if (revision < floor)
                {
                    var message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The watch resynced at revision {revision} after revision {floor}."
                    );
                    violations.Add(message);
                }

                floor = revision;
                continue;
            }

            if (revision <= floor)
            {
                var message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The watch received revision {revision} after revision {floor}."
                );
                violations.Add(message);
            }

            var key = change.Key;
            var partition = key.ToString();

            if (change.Kind == RecordChangeKind.Written)
            {
                var value = HistoryValues.Decode(change.Record?.Value ?? default);
                if (
                    !history.Any(operation =>
                        IsWrite(operation, partition, value, revision, delivery.Stamp)
                    )
                )
                {
                    var message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The watch received '{value}' at revision {revision} for {key}, which no write produced."
                    );
                    violations.Add(message);
                }
            }
            else if (!history.Any(operation => IsDeletion(operation, partition, delivery.Stamp)))
            {
                var message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The watch received a deletion of {key} at revision {revision}, which no call made."
                );
                violations.Add(message);
            }

            floor = revision;
        }

        if (gapDetection)
        {
            foreach (
                var (key, revision) in closing.OrderBy(
                    pair => pair.Key.ToString(),
                    StringComparer.Ordinal
                )
            )
            {
                var covered = changes.Any(delivery =>
                    delivery.Item.IsResync
                        ? delivery.Item.Revision >= revision
                        : delivery.Item.Kind == RecordChangeKind.Written
                            && key.Equals(delivery.Item.Key)
                            && delivery.Item.Revision == revision
                );

                if (!covered)
                {
                    var message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The watch received neither the last write to {key}, at revision {revision}, nor a resync after it."
                    );
                    violations.Add(message);
                }
            }
        }

        return violations.ToImmutable();
    }

    /// <summary>
    /// Determines whether a call may have written a value at a revision before a change was read.
    /// </summary>
    /// <param name="operation">The recorded call.</param>
    /// <param name="partition">The partition of the record's key.</param>
    /// <param name="value">The value that the change carried.</param>
    /// <param name="revision">The revision that the change carried.</param>
    /// <param name="stamp">The stamp at which the watch read the change.</param>
    /// <returns>True when the call is a create or swap of the value that is indeterminate or wrote at the revision.</returns>
    private static bool IsWrite(
        HistoryOperation operation,
        string partition,
        string value,
        long revision,
        long stamp
    )
    {
        if (
            operation.Input is not RecordCall call
            || call.Operation is not (RecordCall.Create or RecordCall.CompareAndSwap)
            || !string.Equals(operation.Partition, partition, StringComparison.Ordinal)
            || !string.Equals(call.Value, value, StringComparison.Ordinal)
            || operation.Invoked >= stamp
        )
        {
            return false;
        }

        return operation.IsIndeterminate
            || (
                operation.Output is RecordWriteResult { IsWritten: true } result
                && result.Revision.Value == revision
            );
    }

    /// <summary>
    /// Determines whether a call may have deleted a record before a change was read.
    /// </summary>
    /// <param name="operation">The recorded call.</param>
    /// <param name="partition">The partition of the record's key.</param>
    /// <param name="stamp">The stamp at which the watch read the change.</param>
    /// <returns>True when the call is a delete that deleted, or a take that returned a record, or either is indeterminate.</returns>
    private static bool IsDeletion(HistoryOperation operation, string partition, long stamp)
    {
        if (
            operation.Input is not RecordCall call
            || call.Operation is not (RecordCall.CompareAndDelete or RecordCall.Take)
            || !string.Equals(operation.Partition, partition, StringComparison.Ordinal)
            || operation.Invoked >= stamp
        )
        {
            return false;
        }

        if (operation.IsIndeterminate)
        {
            return true;
        }

        return call.Operation == RecordCall.CompareAndDelete
            ? operation.Output is RecordDeleteStatus.Deleted
            : operation.Output is StoredRecord;
    }
}
