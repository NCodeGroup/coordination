// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

/// <summary>
/// A provider with a fixed declaration that serves the services it is given.
/// </summary>
/// <param name="declaration">What the provider declares.</param>
/// <param name="services">The implementations of primitive interfaces the provider serves.</param>
internal sealed class TestProvider(
    ProviderDeclaration declaration,
    params IEnumerable<object> services
) : ICoordinationProvider
{
    private ImmutableArray<object> Services { get; } = [.. services];

    /// <inheritdoc />
    public ProviderDeclaration Declaration { get; } = declaration;

    /// <inheritdoc />
    public object? GetPrimitiveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        foreach (var service in Services)
        {
            if (serviceType.IsInstanceOfType(service))
            {
                return service;
            }
        }

        return null;
    }
}
