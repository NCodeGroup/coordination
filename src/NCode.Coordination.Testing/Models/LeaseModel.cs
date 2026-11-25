// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of one lease, which a history keeps in a partition of its own: its state is the holder,
/// the highest fencing token granted and the provider's clock, which starts free.
/// </summary>
internal class LeaseModel : ISequentialModel<LeaseState>
{
    /// <summary>
    /// The owner value of a holder whose acquire never returned.
    /// </summary>
    public const string UnknownOwner = "?";

    /// <inheritdoc />
    public LeaseState Initial { get; } = new(null, 0, 0, TimeSpan.Zero, TimeSpan.Zero);

    /// <inheritdoc />
    public bool TryApply(
        LeaseState state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out LeaseState next
    )
    {
        next = state;

        if (operation.Input is ClockAdvance advance)
        {
            next = state with { Now = state.Now + advance.Duration };
            return true;
        }

        if (operation.Input is not LeaseCall call)
        {
            return false;
        }

        var held = state.Owner is not null && state.Until > state.Now;

        switch (call.Operation)
        {
            case LeaseCall.Acquire:
                if (operation.IsIndeterminate)
                {
                    if (!held)
                    {
                        next = new LeaseState(
                            UnknownOwner,
                            0,
                            state.Floor,
                            state.Now,
                            state.Now + call.TimeToLive
                        );
                    }

                    return true;
                }

                if (operation.Output is not LeaseAcquireResult result)
                {
                    return false;
                }

                if (result.Status == LeaseAcquireStatus.Acquired && !held)
                {
                    var grant = result.Grant;
                    if (grant is null || grant.FencingToken.Value <= state.Floor)
                    {
                        return false;
                    }

                    var token = grant.FencingToken.Value;
                    next = new LeaseState(
                        grant.Owner.Value,
                        token,
                        token,
                        state.Now,
                        state.Now + call.TimeToLive
                    );
                    return true;
                }

                return result.Status == LeaseAcquireStatus.Held && held;

            case LeaseCall.Renew:
            case LeaseCall.Release:
                var matches = held && state.Owner == call.Owner && state.Token == call.Token;
                if (!operation.IsIndeterminate && !(operation.Output is bool b && b == matches))
                {
                    return false;
                }

                if (matches)
                {
                    next =
                        call.Operation == LeaseCall.Renew
                            ? state with
                            {
                                Until = state.Now + call.TimeToLive,
                            }
                            : state with
                            {
                                Owner = null,
                                Token = 0,
                            };
                }

                return true;

            default:
                return false;
        }
    }
}
