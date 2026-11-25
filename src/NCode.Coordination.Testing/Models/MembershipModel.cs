// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of a membership group: its state is a bit mask in which bit <c>c</c> is set when
/// the member of client <c>c</c> has joined, which starts empty.
/// </summary>
/// <param name="sessions">The session of each client, at most 31, that the calls name by index.</param>
internal class MembershipModel(ImmutableArray<SessionId> sessions) : ISequentialModel<int>
{
    /// <inheritdoc />
    public int Initial => 0;

    /// <inheritdoc />
    public bool TryApply(int state, HistoryOperation operation, [MaybeNullWhen(false)] out int next)
    {
        next = state;

        if (operation.Input is not MembershipCall call)
        {
            return false;
        }

        if (call.Operation == MembershipCall.Read)
        {
            return operation.IsIndeterminate
                || (operation.Output is MembershipView view && MaskOf(view) == state);
        }

        if (call.Client < 0 || call.Client >= sessions.Length)
        {
            return false;
        }

        var bit = 1 << call.Client;

        switch (call.Operation)
        {
            case MembershipCall.Join:
                next = state | bit;
                return operation.IsIndeterminate || operation.Output is MembershipJoinStatus.Joined;

            case MembershipCall.Leave:
                next = state & ~bit;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets the bit mask of the clients whose members a view lists.
    /// </summary>
    /// <param name="view">The view that a read returned.</param>
    /// <returns>The mask, or -1 when a member's session is not one of the sessions or is repeated.</returns>
    private int MaskOf(MembershipView view)
    {
        var mask = 0;
        foreach (var member in view.Members)
        {
            var index = sessions.IndexOf(member.Session);
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
