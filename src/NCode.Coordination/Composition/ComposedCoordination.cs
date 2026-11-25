// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Composition;

/// <summary>
/// The result of composing an application's registrations: its providers and what they are bound to.
/// </summary>
/// <param name="providers">The accepted providers, in registration order.</param>
/// <param name="bindings">The bound primitives.</param>
internal sealed class ComposedCoordination(
    ImmutableArray<ICoordinationProvider> providers,
    BindingTable bindings
)
{
    /// <summary>
    /// Gets the accepted providers, in registration order.
    /// </summary>
    public ImmutableArray<ICoordinationProvider> Providers { get; } = providers;

    /// <summary>
    /// Gets the bound primitives.
    /// </summary>
    public BindingTable Bindings { get; } = bindings;
}
