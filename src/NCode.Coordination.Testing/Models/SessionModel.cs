// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of one session, which a history keeps in a partition of its own: its state is the
/// identity the open call returned and whether the session has ended, which starts unopened.
/// </summary>
internal class SessionModel : ISequentialModel<SessionState>
{
    /// <inheritdoc />
    public SessionState Initial { get; } = new(null, false);

    /// <inheritdoc />
    public bool TryApply(
        SessionState state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out SessionState next
    )
    {
        next = state;

        if (operation.Input is not SessionCall call)
        {
            return false;
        }

        switch (call.Operation)
        {
            case SessionCall.Open:
                if (operation.IsIndeterminate)
                {
                    return true;
                }

                if (state.Id is null && operation.Output is SessionGrant grant)
                {
                    next = new SessionState(grant.Id, false);
                    return true;
                }

                return false;

            case SessionCall.KeepAlive:
                return state.Id is not null
                    && (
                        operation.IsIndeterminate
                        || (
                            operation.Output is KeepAliveResult result
                            && result.IsRenewed == !state.Ended
                        )
                    );

            case SessionCall.Close:
                if (state.Id is null)
                {
                    return false;
                }

                next = state with { Ended = true };
                return true;

            default:
                return false;
        }
    }
}
