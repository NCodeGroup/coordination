// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace NCode.Coordination.Composition;

/// <summary>
/// The result of composing an application's registrations: its providers, what they are bound to and what failed.
/// </summary>
/// <param name="providers">The providers that were created, in registration order.</param>
/// <param name="bindings">The bound primitives.</param>
/// <param name="failures">The failures found, in registration order.</param>
/// <param name="factoryFailure">The exception a provider factory threw, or null when none did.</param>
internal sealed class ComposedCoordination(
    ImmutableArray<ICoordinationProvider> providers,
    BindingTable bindings,
    ImmutableArray<string> failures,
    ExceptionDispatchInfo? factoryFailure
)
{
    /// <summary>
    /// Gets the providers that were created, in registration order.
    /// </summary>
    public ImmutableArray<ICoordinationProvider> Providers { get; } = providers;

    /// <summary>
    /// Gets the bound primitives.
    /// </summary>
    public BindingTable Bindings { get; } = bindings;

    /// <summary>
    /// Gets the failures found, in registration order.
    /// </summary>
    public ImmutableArray<string> Failures { get; } = failures;

    /// <summary>
    /// Gets the exception a provider factory threw, or null when none did.
    /// </summary>
    public ExceptionDispatchInfo? FactoryFailure { get; } = factoryFailure;
}
