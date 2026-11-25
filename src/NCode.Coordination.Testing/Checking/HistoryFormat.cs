// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Globalization;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// Describes the calls of a history as text, for the report of a failed check.
/// </summary>
internal static class HistoryFormat
{
    /// <summary>
    /// Describes a call: its client, the points where it was invoked and responded, its input and how it ended.
    /// </summary>
    /// <param name="operation">The call.</param>
    /// <returns>The description.</returns>
    public static string Describe(HistoryOperation operation)
    {
        var responded = operation.IsIndeterminate
            ? "never"
            : operation.Responded.ToString(CultureInfo.InvariantCulture);

        var output = operation.Outcome switch
        {
            HistoryOutcome.Failed => "failed",
            HistoryOutcome.Indeterminate => "indeterminate",
            _ => DescribeValue(operation.Output),
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"client {operation.Client} [{operation.Invoked}, {responded}] {DescribeValue(operation.Input)} -> {output}"
        );
    }

    /// <summary>
    /// Describes a value that a call took or returned.
    /// </summary>
    /// <param name="value">The value, or null when there is none.</param>
    /// <returns>The description.</returns>
    public static string DescribeValue(object? value) =>
        value switch
        {
            null => "nothing",
            StoredRecord record => string.Create(
                CultureInfo.InvariantCulture,
                $"'{HistoryValues.Decode(record.Value)}' at {record.Revision}"
            ),
            RecordWriteResult result => string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Status} {result.Revision}"
            ),
            TransactionResult result => string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Status} {result.Revision}"
            ),
            LeaseAcquireResult result => string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Status} {result.Grant?.FencingToken}"
            ),
            SemaphoreAcquireResult result => string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Status} slot {result.Grant?.Slot} {result.Grant?.FencingToken}"
            ),
            ElectionResult result => string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Status} {result.Leadership?.Leader.Term}"
            ),
            ElectionView view => view.Leader is { } leader
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"leader {leader.Term} '{HistoryValues.Decode(leader.Value)}'"
                )
                : "no leader",
            MembershipView view => "members "
                + string.Join(", ", view.Members.Select(member => member.Session)),
            SessionGrant grant => grant.Id.ToString(),
            KeepAliveResult result => result.Status.ToString(),
            SequenceBlock block => string.Create(
                CultureInfo.InvariantCulture,
                $"{block.First}..{block.Last}"
            ),
            ImmutableArray<KeySegment> segments => string.Join(", ", segments),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
}
