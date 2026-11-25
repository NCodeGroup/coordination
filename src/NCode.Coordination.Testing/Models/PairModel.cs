// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The sequential specification of two keys that one partition of a history holds: a call on one key applies the model
/// of its key, and a derived model specifies the operations that span both keys.
/// </summary>
/// <typeparam name="TState">The type of the state of one key.</typeparam>
/// <param name="keyModel">The sequential specification of one key.</param>
internal abstract class PairModel<TState>(ISequentialModel<TState> keyModel)
    : ISequentialModel<PairState<TState>>
    where TState : notnull
{
    /// <summary>
    /// Gets the sequential specification of one key.
    /// </summary>
    protected ISequentialModel<TState> KeyModel { get; } = keyModel;

    /// <inheritdoc />
    public PairState<TState> Initial { get; } = new(keyModel.Initial, keyModel.Initial);

    /// <inheritdoc />
    public bool TryApply(
        PairState<TState> state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out PairState<TState> next
    )
    {
        next = state;

        if (operation.Input is not KeyedCall { Key: 0 or 1 } keyed)
        {
            return TryApplySpanning(state, operation, out next);
        }

        var current = state.Get(keyed.Key);
        var unwrapped = operation with { Input = keyed.Call };
        if (!KeyModel.TryApply(current, unwrapped, out var applied))
        {
            return false;
        }

        next = state.With(keyed.Key, applied);
        return true;
    }

    /// <summary>
    /// Applies a call that spans both keys when the specification allows the call to return what it returned. It
    /// returns false for an input type, operation name or output the rules don't allow, and for an indeterminate
    /// operation it checks no output and applies the effect the call has if it applies.
    /// </summary>
    /// <param name="state">The state of both keys before the call.</param>
    /// <param name="operation">The call, with the result it returned.</param>
    /// <param name="next">The state of both keys after the call when the call is allowed.</param>
    /// <returns>True when the specification allows the call to return what it returned.</returns>
    protected abstract bool TryApplySpanning(
        PairState<TState> state,
        HistoryOperation operation,
        [MaybeNullWhen(false)] out PairState<TState> next
    );
}
