// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition;

/// <summary>
/// What an application registered on its coordination builder, in registration order.
/// </summary>
internal sealed class CoordinationRegistrations
{
    /// <summary>
    /// Gets the factories that create the providers.
    /// </summary>
    public List<Func<IServiceProvider, ICoordinationProvider>> ProviderFactories { get; } = [];

    /// <summary>
    /// Gets the bindings of providers to primitives and keyspaces.
    /// </summary>
    public List<ProviderBinding> Bindings { get; } = [];

    /// <summary>
    /// Gets the requirements of the application's features.
    /// </summary>
    public List<FeatureRequirement> Requirements { get; } = [];

    /// <summary>
    /// Gets the guarantees the application defines.
    /// </summary>
    public List<GuaranteeDefinition> Definitions { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the application is declared a single node.
    /// </summary>
    public bool IsSingleNode { get; set; }
}
