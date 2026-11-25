// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of one election, which a history keeps in a partition of its own: its state is the
/// leader, its term and value, and the highest term known, which starts with no leader.
/// </summary>
internal class ElectionModel : ISequentialModel<ElectionState>
{
    /// <summary>
    /// The owner value of a leader whose lead never returned.
    /// </summary>
    public const string UnknownOwner = "?";

    /// <inheritdoc />
    public ElectionState Initial { get; } = new(null, 0, 0, null);

    /// <inheritdoc />
    public bool TryApply(
        ElectionState state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out ElectionState next
    )
    {
        next = state;

        if (operation.Input is not ElectionCall call)
        {
            return false;
        }

        var vacant = state.Owner is null;

        switch (call.Operation)
        {
            case ElectionCall.Lead:
                if (operation.IsIndeterminate)
                {
                    if (vacant)
                    {
                        next = new ElectionState(UnknownOwner, 0, state.Floor, call.Value);
                    }

                    return true;
                }

                if (operation.Output is not ElectionResult result)
                {
                    return false;
                }

                if (result.Status == ElectionStatus.Elected && vacant)
                {
                    var leadership = result.Leadership;
                    if (leadership is null)
                    {
                        return false;
                    }

                    var term = leadership.Leader.Term.Value;
                    var value = HistoryValues.Decode(leadership.Leader.Value);
                    if (term <= state.Floor || value != call.Value)
                    {
                        return false;
                    }

                    next = new ElectionState(leadership.Owner.Value, term, term, call.Value);
                    return true;
                }

                return result.Status == ElectionStatus.Led && !vacant;

            case ElectionCall.Renew:
            case ElectionCall.Resign:
                var matches =
                    state.Owner is not null && state.Owner == call.Owner && state.Term == call.Term;
                if (!operation.IsIndeterminate && !(operation.Output is bool b && b == matches))
                {
                    return false;
                }

                if (matches && call.Operation == ElectionCall.Resign)
                {
                    next = new ElectionState(null, 0, state.Floor, null);
                }

                return true;

            case ElectionCall.Read:
                if (operation.IsIndeterminate)
                {
                    return true;
                }

                if (operation.Output is not ElectionView view)
                {
                    return false;
                }

                var leader = view.Leader;
                if (leader is null)
                {
                    return vacant;
                }

                if (vacant)
                {
                    return false;
                }

                var leaderTerm = leader.Term.Value;
                var leaderValue = HistoryValues.Decode(leader.Value);
                var known =
                    leaderValue == state.Value
                    && (leaderTerm == state.Term || (state.Term == 0 && leaderTerm > state.Floor));
                if (!known)
                {
                    return false;
                }

                next = state with { Term = leaderTerm, Floor = Math.Max(state.Floor, leaderTerm) };
                return true;

            default:
                return false;
        }
    }
}
