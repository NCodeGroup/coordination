// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of one semaphore, which a history keeps in a partition of its own: its state is the
/// owner, fencing token and highest token granted of each of its two slots, which starts empty.
/// </summary>
internal class SemaphoreModel : ISequentialModel<SemaphoreState>
{
    /// <summary>
    /// The number of slots the semaphore has.
    /// </summary>
    public const int Capacity = 2;

    /// <summary>
    /// Reads the owner, token and floor of a slot.
    /// </summary>
    /// <param name="state">The state to read.</param>
    /// <param name="slot">The slot, 0 or 1.</param>
    /// <returns>The owner, fencing token value and highest token value granted of the slot.</returns>
    private static (string? Owner, long Token, long Floor) ReadSlot(
        SemaphoreState state,
        int slot
    ) =>
        slot == 0
            ? (state.FirstOwner, state.FirstToken, state.FirstFloor)
            : (state.SecondOwner, state.SecondToken, state.SecondFloor);

    /// <summary>
    /// Replaces the owner, token and floor of a slot.
    /// </summary>
    /// <param name="state">The state to change.</param>
    /// <param name="slot">The slot, 0 or 1.</param>
    /// <param name="value">The new owner value, or null when the slot becomes free, the new fencing token value and the new highest token value granted.</param>
    /// <returns>The state with the slot replaced.</returns>
    private static SemaphoreState WriteSlot(
        SemaphoreState state,
        int slot,
        (string? Owner, long Token, long Floor) value
    ) =>
        slot == 0
            ? state with
            {
                FirstOwner = value.Owner,
                FirstToken = value.Token,
                FirstFloor = value.Floor,
            }
            : state with
            {
                SecondOwner = value.Owner,
                SecondToken = value.Token,
                SecondFloor = value.Floor,
            };

    /// <summary>
    /// Counts the slots held, those of acquires that never returned included.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The number of slots held.</returns>
    private static int CountHeld(SemaphoreState state)
    {
        var held = state.Unknown;
        if (state.FirstOwner is not null)
        {
            held++;
        }

        if (state.SecondOwner is not null)
        {
            held++;
        }

        return held;
    }

    /// <summary>
    /// Applies an acquire.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryAcquire(
        SemaphoreState state,
        HistoryOperation operation,
        out SemaphoreState next
    )
    {
        next = state;

        var held = CountHeld(state);

        if (operation.IsIndeterminate)
        {
            if (held < Capacity)
            {
                next = state with { Unknown = state.Unknown + 1 };
            }

            return true;
        }

        if (operation.Output is not SemaphoreAcquireResult result)
        {
            return false;
        }

        if (result.Status == SemaphoreAcquireStatus.Acquired && held < Capacity)
        {
            var grant = result.Grant;
            if (grant is null || grant.Slot is < 0 or >= Capacity)
            {
                return false;
            }

            var (owner, _, floor) = ReadSlot(state, grant.Slot);
            var token = grant.FencingToken.Value;
            if (owner is not null || token <= floor)
            {
                return false;
            }

            next = WriteSlot(state, grant.Slot, (grant.Owner.Value, token, token));
            return true;
        }

        return result.Status == SemaphoreAcquireStatus.Full && held >= Capacity;
    }

    /// <summary>
    /// Applies a renew or a release.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="call">The call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryRenewOrRelease(
        SemaphoreState state,
        HistoryOperation operation,
        SemaphoreCall call,
        out SemaphoreState next
    )
    {
        next = state;

        var matches = false;
        var slotOwner = default(string);
        var slotToken = 0L;
        var slotFloor = 0L;
        if (call.Slot is >= 0 and < Capacity)
        {
            (slotOwner, slotToken, slotFloor) = ReadSlot(state, call.Slot);
            matches = slotOwner is not null && slotOwner == call.Owner && slotToken == call.Token;
        }

        if (!operation.IsIndeterminate && !(operation.Output is bool b && b == matches))
        {
            return false;
        }

        if (matches && call.Operation == SemaphoreCall.Release)
        {
            next = WriteSlot(state, call.Slot, (null, 0, slotFloor));
        }

        return true;
    }

    /// <inheritdoc />
    public SemaphoreState Initial { get; } = new(null, 0, 0, null, 0, 0, 0);

    /// <inheritdoc />
    public bool TryApply(
        SemaphoreState state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out SemaphoreState next
    )
    {
        next = state;

        if (operation.Input is not SemaphoreCall call)
        {
            return false;
        }

        switch (call.Operation)
        {
            case SemaphoreCall.Acquire:
                return TryAcquire(state, operation, out next);

            case SemaphoreCall.Renew:
            case SemaphoreCall.Release:
                return TryRenewOrRelease(state, operation, call, out next);

            default:
                return false;
        }
    }
}
