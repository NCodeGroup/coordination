// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of one record, which a history keeps in a partition of its own: its state is the
/// value and revision of the record and the highest revision known, which starts absent.
/// </summary>
internal class RecordModel : ISequentialModel<RecordState>
{
    /// <inheritdoc />
    public RecordState Initial { get; } = new(null, 0, 0);

    /// <inheritdoc />
    public bool TryApply(
        RecordState state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out RecordState next
    )
    {
        next = state;

        if (operation.Input is not RecordCall call)
        {
            return false;
        }

        switch (call.Operation)
        {
            case RecordCall.Create:
                return TryCreate(state, operation, call, out next);

            case RecordCall.Read:
                return TryRead(state, operation, out next);

            case RecordCall.CompareAndSwap:
                return TryCompareAndSwap(state, operation, call, out next);

            case RecordCall.CompareAndDelete:
                return TryCompareAndDelete(state, operation, call, out next);

            case RecordCall.Take:
                return TryTake(state, operation, out next);

            default:
                return false;
        }
    }

    /// <summary>
    /// Applies a create.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="call">The call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryCreate(
        RecordState state,
        HistoryOperation operation,
        RecordCall call,
        out RecordState next
    )
    {
        next = state;
        var absent = state.Value is null;

        if (operation.IsIndeterminate)
        {
            if (absent)
            {
                next = new RecordState(call.Value, 0, state.Floor);
            }

            return true;
        }

        if (operation.Output is not RecordWriteResult result)
        {
            return false;
        }

        if (result.IsWritten && absent)
        {
            return TryWrite(state, call, result.Revision.Value, out next);
        }

        return result.Status == RecordWriteStatus.Conflict && !absent;
    }

    /// <summary>
    /// Applies a read.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryRead(RecordState state, HistoryOperation operation, out RecordState next)
    {
        next = state;
        var absent = state.Value is null;

        if (operation.IsIndeterminate)
        {
            return true;
        }

        if (operation.Output is null)
        {
            return absent;
        }

        if (operation.Output is not StoredRecord record || absent || !Matches(state, record))
        {
            return false;
        }

        var revision = record.Revision.Value;
        var floor = Math.Max(state.Floor, revision);
        next = state with { Revision = revision, Floor = floor };
        return true;
    }

    /// <summary>
    /// Applies a compare-and-swap.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="call">The call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryCompareAndSwap(
        RecordState state,
        HistoryOperation operation,
        RecordCall call,
        out RecordState next
    )
    {
        next = state;
        var absent = state.Value is null;
        var mayMatch = MayMatch(state, call);

        if (operation.IsIndeterminate)
        {
            if (mayMatch)
            {
                next = new RecordState(call.Value, 0, state.Floor);
            }

            return true;
        }

        if (operation.Output is not RecordWriteResult result)
        {
            return false;
        }

        if (result.IsWritten && mayMatch)
        {
            return TryWrite(state, call, result.Revision.Value, out next);
        }

        return result.Status switch
        {
            RecordWriteStatus.Conflict => Differs(state, call),
            RecordWriteStatus.NotFound => absent,
            _ => false,
        };
    }

    /// <summary>
    /// Applies a compare-and-delete.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="call">The call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryCompareAndDelete(
        RecordState state,
        HistoryOperation operation,
        RecordCall call,
        out RecordState next
    )
    {
        next = state;
        var absent = state.Value is null;
        var mayMatch = MayMatch(state, call);

        if (operation.IsIndeterminate)
        {
            if (mayMatch)
            {
                next = new RecordState(null, 0, state.Floor);
            }

            return true;
        }

        if (operation.Output is not RecordDeleteStatus status)
        {
            return false;
        }

        switch (status)
        {
            case RecordDeleteStatus.Deleted when mayMatch:
                next = new RecordState(null, 0, state.Floor);
                return true;

            case RecordDeleteStatus.Conflict:
                return Differs(state, call);

            case RecordDeleteStatus.NotFound:
                return absent;

            default:
                return false;
        }
    }

    /// <summary>
    /// Applies a take.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryTake(RecordState state, HistoryOperation operation, out RecordState next)
    {
        next = state;
        var absent = state.Value is null;

        if (operation.IsIndeterminate)
        {
            next = new RecordState(null, 0, state.Floor);
            return true;
        }

        if (operation.Output is null)
        {
            return absent;
        }

        if (operation.Output is not StoredRecord record || absent || !Matches(state, record))
        {
            return false;
        }

        var floor = Math.Max(state.Floor, record.Revision.Value);
        next = new RecordState(null, 0, floor);
        return true;
    }

    /// <summary>
    /// Applies a write that returned a revision, which must be above the floor.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="call">The call that wrote.</param>
    /// <param name="revision">The revision value the write returned.</param>
    /// <param name="next">The state after the write.</param>
    /// <returns>True when the revision is above the floor.</returns>
    private static bool TryWrite(
        RecordState state,
        RecordCall call,
        long revision,
        out RecordState next
    )
    {
        next = state;
        if (revision <= state.Floor)
        {
            return false;
        }

        next = new RecordState(call.Value, revision, revision);
        return true;
    }

    /// <summary>
    /// Determines whether a record that a call returned is the record the state describes.
    /// </summary>
    /// <param name="state">The state before the call, with a record present.</param>
    /// <param name="record">The record the call returned.</param>
    /// <returns>True when the value and revision agree, or the revision is unknown and above the floor.</returns>
    private static bool Matches(RecordState state, StoredRecord record)
    {
        var revision = record.Revision.Value;
        var value = HistoryValues.Decode(record.Value);
        return value == state.Value
            && (revision == state.Revision || (state.Revision == 0 && revision > state.Floor));
    }

    /// <summary>
    /// Determines whether the record is present with a revision that may be the one the call expects.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="call">The call.</param>
    /// <returns>True when the call may find the revision it expects.</returns>
    private static bool MayMatch(RecordState state, RecordCall call) =>
        state.Value is not null && (state.Revision == 0 || state.Revision == call.Expected);

    /// <summary>
    /// Determines whether the record is present with a revision that may differ from the one the call expects.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="call">The call.</param>
    /// <returns>True when the call may find another revision than it expects.</returns>
    private static bool Differs(RecordState state, RecordCall call) =>
        state.Value is not null && (state.Revision == 0 || state.Revision != call.Expected);
}
