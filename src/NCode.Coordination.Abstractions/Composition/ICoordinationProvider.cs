// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A provider of primitives, which the composition creates once from the factory the provider's configurator adds.
/// </summary>
public interface ICoordinationProvider
{
    /// <summary>
    /// Gets what the provider declares: its primitives, their guarantees and the guarantees it defines.
    /// </summary>
    ProviderDeclaration Declaration { get; }

    /// <summary>
    /// Gets the provider's implementation of a primitive's interface, such as <c>IOccurrenceService</c>, or null when
    /// it has none.
    /// </summary>
    /// <param name="serviceType">The interface of the primitive.</param>
    /// <returns>The implementation, or null when the provider has none.</returns>
    object? GetPrimitiveService(Type serviceType);

    /// <summary>
    /// Verifies that the provider's server keeps what the provider declares, such as its vendor, its version and the
    /// server settings a declared guarantee needs. The composition calls it once, as the host starts and before any
    /// hosted service starts.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the probe.</param>
    /// <returns>A task that completes when the probe has passed.</returns>
    /// <exception cref="CoordinationException">The server does not keep what the provider declares.</exception>
    ValueTask ProbeAsync(CancellationToken cancellationToken);
}
