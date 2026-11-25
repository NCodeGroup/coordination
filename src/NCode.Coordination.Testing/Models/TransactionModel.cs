// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of a transaction over two records, which a history keeps in a partition of its own:
/// its state is the value of each record, which both start absent. A transaction writes both records or neither.
/// </summary>
internal class TransactionModel : ISequentialModel<TransactionState>
{
    /// <inheritdoc />
    public TransactionState Initial { get; } = new(null, null);

    /// <inheritdoc />
    public bool TryApply(
        TransactionState state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out TransactionState next
    )
    {
        next = state;

        if (operation.Input is not TransactionCall call)
        {
            return false;
        }

        switch (call.Operation)
        {
            case TransactionCall.Read:
                return TryRead(state, operation, call);

            case TransactionCall.Execute:
                return TryExecute(state, operation, call, out next);

            default:
                return false;
        }
    }

    /// <summary>
    /// Applies a read, which changes nothing.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="call">The call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryRead(
        TransactionState state,
        HistoryOperation operation,
        TransactionCall call
    )
    {
        string? value;
        switch (call.Key)
        {
            case TransactionCall.FirstKey:
                value = state.First;
                break;

            case TransactionCall.SecondKey:
                value = state.Second;
                break;

            default:
                return false;
        }

        if (operation.IsIndeterminate)
        {
            return true;
        }

        if (operation.Output is null)
        {
            return value is null;
        }

        return operation.Output is StoredRecord record
            && value is not null
            && HistoryValues.Decode(record.Value) == value;
    }

    /// <summary>
    /// Applies an execute.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The recorded call.</param>
    /// <param name="call">The call.</param>
    /// <param name="next">The state after the call.</param>
    /// <returns>True when the output is one the state allows.</returns>
    private static bool TryExecute(
        TransactionState state,
        HistoryOperation operation,
        TransactionCall call,
        out TransactionState next
    )
    {
        next = state;
        var holds = string.Equals(state.First, call.Expected, StringComparison.Ordinal);

        if (operation.IsIndeterminate)
        {
            if (holds)
            {
                next = new TransactionState(call.Value, call.Value);
            }

            return true;
        }

        if (operation.Output is not TransactionResult result)
        {
            return false;
        }

        switch (result.Status)
        {
            case TransactionStatus.Succeeded when holds:
                next = new TransactionState(call.Value, call.Value);
                return true;

            case TransactionStatus.Failed:
                return !holds;

            default:
                return false;
        }
    }
}
