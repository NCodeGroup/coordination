// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of the records of a run's two keys, which a history keeps in one partition, and of
/// listings that read them both. A page lists each key at most once, each record as a read of its key allows, at a
/// revision no lower than any revision known before it. A page without a continuation token lists every present key,
/// and one with a token may hold fewer.
/// </summary>
/// <param name="keys">The two keys of the run, in the order that <see cref="KeyedCall.Key"/> indexes them.</param>
internal class RecordListingModel(ImmutableArray<CoordinationKey> keys)
    : PairModel<RecordState>(new RecordModel())
{
    /// <inheritdoc />
    protected override bool TryApplySpanning(
        PairState<RecordState> state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out PairState<RecordState> next
    )
    {
        next = state;

        if (operation.Input is not RecordCall { Operation: RecordCall.List })
        {
            return false;
        }

        if (operation.IsIndeterminate)
        {
            return true;
        }

        if (operation.Output is not RecordPage page)
        {
            return false;
        }

        if (!TryLearn(state, operation, page, out var learned) || !IsCurrent(state, page))
        {
            return false;
        }

        next = learned;
        return true;
    }

    /// <summary>
    /// Applies the records of a page as reads of their keys.
    /// </summary>
    /// <param name="state">The state of both keys before the listing.</param>
    /// <param name="operation">The recorded listing.</param>
    /// <param name="page">The page the listing returned.</param>
    /// <param name="learned">The state of both keys after the reads.</param>
    /// <returns>True when each record is one that a read of its key allows, at a revision not above the page's.</returns>
    private bool TryLearn(
        PairState<RecordState> state,
        HistoryOperation operation,
        RecordPage page,
        out PairState<RecordState> learned
    )
    {
        learned = state;
        var read = new RecordCall(RecordCall.Read, null, 0);
        var listed = new bool[2];

        foreach (var stored in page.Records)
        {
            var index = keys.IndexOf(stored.Key);
            if (index is < 0 or > 1 || listed[index])
            {
                return false;
            }

            listed[index] = true;

            if (stored.Revision.Value > page.Revision.Value)
            {
                return false;
            }

            var shown = operation with { Input = read, Output = stored };
            var current = learned.Get(index);
            if (!KeyModel.TryApply(current, shown, out var applied))
            {
                return false;
            }

            learned = learned.With(index, applied);
        }

        return true;
    }

    /// <summary>
    /// Determines whether a page reflects the state that was known before it.
    /// </summary>
    /// <param name="state">The state of both keys before the listing.</param>
    /// <param name="page">The page the listing returned.</param>
    /// <returns>
    /// True when the page's revision is no lower than either key's floor and, for a page without a continuation
    /// token, every present key is listed.
    /// </returns>
    private bool IsCurrent(PairState<RecordState> state, RecordPage page)
    {
        var revision = page.Revision.Value;
        if (revision < state.First.Floor || revision < state.Second.Floor)
        {
            return false;
        }

        if (page.ContinuationToken is not null)
        {
            return true;
        }

        return IsListedIfPresent(state.First, 0, page) && IsListedIfPresent(state.Second, 1, page);
    }

    /// <summary>
    /// Determines whether a key that is present is listed on a page.
    /// </summary>
    /// <param name="state">The state of the key before the listing.</param>
    /// <param name="index">The index of the key.</param>
    /// <param name="page">The page the listing returned.</param>
    /// <returns>True when the key is absent or the page lists it.</returns>
    private bool IsListedIfPresent(RecordState state, int index, RecordPage page) =>
        state.Value is null || page.Records.Any(stored => keys.IndexOf(stored.Key) == index);
}
