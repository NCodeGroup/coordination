// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Checking;

/// <summary>
/// A primitive's sequential specification: the states it can be in and the calls each state allows.
/// </summary>
/// <remarks>
/// The state compares by value, so the search can recognize a state it has already reached. A model never rejects what
/// a correct provider can return. A call whose outcome is indeterminate may have applied, so a model checks no output
/// for it and applies the effect the call has, if it applies.
/// </remarks>
/// <typeparam name="TState">The type of the state, compared by value.</typeparam>
internal interface ISequentialModel<TState>
    where TState : notnull
{
    /// <summary>
    /// Gets the state before any call.
    /// </summary>
    TState Initial { get; }

    /// <summary>
    /// Applies one call to a state when the specification allows the call to return what it returned in that state.
    /// </summary>
    /// <param name="state">The state before the call.</param>
    /// <param name="operation">The call, with the result it returned.</param>
    /// <param name="next">The state after the call, when the call is allowed.</param>
    /// <returns>True when the specification allows the call in the state.</returns>
    bool TryApply(TState state, HistoryOperation operation, [MaybeNullWhen(false)] out TState next);
}
