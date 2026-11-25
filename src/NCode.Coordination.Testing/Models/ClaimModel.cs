// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of a claim: its state is whether it has been claimed, which starts false.
/// </summary>
internal class ClaimModel : ISequentialModel<bool>
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

        if (operation.Input is not ClaimCall call || call.Operation != ClaimCall.Claim)
        {
            return false;
        }

        next = true;
        return operation.IsIndeterminate
            || (
                operation.Output is ClaimStatus status
                && status == (state ? ClaimStatus.Taken : ClaimStatus.Claimed)
            );
    }
}
