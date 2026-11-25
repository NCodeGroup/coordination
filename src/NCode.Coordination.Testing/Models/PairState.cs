// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The states of the two keys of a run in its sequential specification.
/// </summary>
/// <typeparam name="TState">The type of the state of one key.</typeparam>
/// <param name="First">The state of the first key.</param>
/// <param name="Second">The state of the second key.</param>
internal sealed record PairState<TState>(TState First, TState Second)
    where TState : notnull
{
    /// <summary>
    /// Gets the state of one key.
    /// </summary>
    /// <param name="key">The index of the key, 0 or 1.</param>
    /// <returns>The state of the first key for 0, otherwise the state of the second key.</returns>
    public TState Get(int key) => key == 0 ? First : Second;

    /// <summary>
    /// Replaces the state of one key.
    /// </summary>
    /// <param name="key">The index of the key, 0 or 1.</param>
    /// <param name="state">The new state of the key.</param>
    /// <returns>A copy of the pair with the state of the key replaced.</returns>
    public PairState<TState> With(int key, TState state) =>
        key == 0 ? this with { First = state } : this with { Second = state };
}
