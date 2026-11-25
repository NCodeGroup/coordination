// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of a set: its state is a bit mask in which bit <c>i</c> is set when
/// <c>members[i]</c> is present, which starts empty.
/// </summary>
/// <param name="members">The members the calls name by index, at most 31.</param>
internal class SetModel(ImmutableArray<KeySegment> members) : ISequentialModel<int>
{
    /// <inheritdoc />
    public int Initial => 0;

    /// <inheritdoc />
    public bool TryApply(int state, HistoryOperation operation, [MaybeNullWhen(false)] out int next)
    {
        next = state;

        if (operation.Input is not SetCall call)
        {
            return false;
        }

        if (call.Operation == SetCall.Clear)
        {
            next = 0;
            return true;
        }

        if (call.Operation == SetCall.List)
        {
            return operation.IsIndeterminate
                || (
                    operation.Output is ImmutableArray<KeySegment> listed && MaskOf(listed) == state
                );
        }

        if (call.Member < 0 || call.Member >= members.Length)
        {
            return false;
        }

        var bit = 1 << call.Member;

        switch (call.Operation)
        {
            case SetCall.Add:
                next = state | bit;
                return operation.IsIndeterminate
                    || (operation.Output is bool added && added == ((state & bit) == 0));

            case SetCall.Remove:
                next = state & ~bit;
                return operation.IsIndeterminate
                    || (operation.Output is bool removed && removed == ((state & bit) != 0));

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets the bit mask of the listed members.
    /// </summary>
    /// <param name="listed">The members that a list returned.</param>
    /// <returns>The mask, or -1 when a segment is not one of the members or is repeated.</returns>
    private int MaskOf(ImmutableArray<KeySegment> listed)
    {
        if (listed.IsDefault)
        {
            return -1;
        }

        var mask = 0;
        foreach (var segment in listed)
        {
            var index = members.IndexOf(segment);
            if (index < 0)
            {
                return -1;
            }

            var bit = 1 << index;
            if ((mask & bit) != 0)
            {
                return -1;
            }

            mask |= bit;
        }

        return mask;
    }
}
