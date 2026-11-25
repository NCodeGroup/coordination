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

        var input = DescribeValue(operation.Input);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"client {operation.Client} [{operation.Invoked}, {responded}] {input} -> {output}"
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
            StoredRecord record => DescribeRecord(record),
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
            ElectionView view => DescribeLeader(view),
            MembershipView view => DescribeMembers(view),
            SessionGrant grant => grant.Id.ToString(),
            KeepAliveResult result => result.Status.ToString(),
            SequenceBlock block => string.Create(
                CultureInfo.InvariantCulture,
                $"{block.First}..{block.Last}"
            ),
            ImmutableArray<KeySegment> segments => string.Join(", ", segments),
            RecordPage page => DescribePage(page),
            CounterBatchResult result => DescribeBatch(result),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    /// <summary>
    /// Describes the members of a membership view by their sessions.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <returns>The description.</returns>
    private static string DescribeMembers(MembershipView view)
    {
        var sessions = view.Members.Select(member => member.Session);

        return "members " + string.Join(", ", sessions);
    }

    /// <summary>
    /// Describes a record by its value and revision.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>The description.</returns>
    private static string DescribeRecord(StoredRecord record)
    {
        var value = HistoryValues.Decode(record.Value);

        return string.Create(CultureInfo.InvariantCulture, $"'{value}' at {record.Revision}");
    }

    /// <summary>
    /// Describes the leader of an election view by its term and value.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <returns>The description, or "no leader" when the view has none.</returns>
    private static string DescribeLeader(ElectionView view)
    {
        if (view.Leader is not { } leader)
        {
            return "no leader";
        }

        var value = HistoryValues.Decode(leader.Value);

        return string.Create(CultureInfo.InvariantCulture, $"leader {leader.Term} '{value}'");
    }

    /// <summary>
    /// Describes a record of a page by its key, value and revision.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <returns>The description.</returns>
    private static string DescribeListedRecord(StoredRecord record)
    {
        var described = DescribeRecord(record);

        return string.Create(CultureInfo.InvariantCulture, $"{record.Key} {described}");
    }

    /// <summary>
    /// Describes a page of records by its revision and each record's key, value and revision.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>The description.</returns>
    private static string DescribePage(RecordPage page)
    {
        var records = page.Records.Select(DescribeListedRecord);

        var listed = page.Records.Length == 0 ? "nothing" : string.Join(", ", records);
        var more = page.ContinuationToken is null ? string.Empty : " and more";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"page at {page.Revision}: {listed}{more}"
        );
    }

    /// <summary>
    /// Describes the result of a counter batch by its counts and the increment that exceeded its limit.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The description.</returns>
    private static string DescribeBatch(CounterBatchResult result)
    {
        var formatted = result.Counts.Select(count => count.ToString(CultureInfo.InvariantCulture));
        var counts = "counts " + string.Join(", ", formatted);

        if (result.ExceededIndex is not { } exceeded)
        {
            return counts;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{counts} exceeded at {exceeded}");
    }
}
