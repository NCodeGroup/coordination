// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of an occurrence: its state is whether it has been recorded, which starts false.
/// </summary>
internal class OccurrenceModel : ISequentialModel<bool>
{
    /// <inheritdoc />
    public bool Initial => false;

    /// <inheritdoc />
    public bool TryApply(
        bool state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out bool next
    )
    {
        next = state;

        if (operation.Input is not OccurrenceCall call || call.Operation != OccurrenceCall.Record)
        {
            return false;
        }

        next = true;
        return operation.IsIndeterminate
            || (
                operation.Output is OccurrenceStatus status
                && status == (state ? OccurrenceStatus.Duplicate : OccurrenceStatus.Recorded)
            );
    }
}
