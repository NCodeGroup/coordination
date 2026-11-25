// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of a sequence: its state is the last number handed out, which starts at zero.
/// </summary>
internal class SequenceModel : ISequentialModel<long>
{
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

        if (operation.Input is not SequenceCall call)
        {
            return false;
        }

        switch (call.Operation)
        {
            case SequenceCall.Next:
                next = state + 1;
                return operation.IsIndeterminate
                    || (operation.Output is long number && number == next);

            case SequenceCall.Reserve:
                if (operation.IsIndeterminate)
                {
                    next = state + call.Count;
                    return true;
                }

                if (
                    operation.Output is SequenceBlock block
                    && block.First == state + 1
                    && block.Count == call.Count
                )
                {
                    next = block.Last;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }
}
