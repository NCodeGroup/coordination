// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition;

/// <summary>
/// Records what an application registers on its coordination builder.
/// </summary>
internal class DefaultCoordinationBuilder(CoordinationRegistrations registrations)
    : ICoordinationBuilder
{
    /// <summary>
    /// Gets the registrations the builder adds to.
    /// </summary>
    private CoordinationRegistrations Registrations { get; } = registrations;

    /// <inheritdoc />
    public ICoordinationBuilder AddProvider(Func<IServiceProvider, ICoordinationProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Registrations.ProviderFactories.Add(factory);
        return this;
    }

    /// <inheritdoc />
    public ICoordinationBuilder Bind(ProviderBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Registrations.Bindings.Add(binding);
        return this;
    }

    /// <inheritdoc />
    public ICoordinationBuilder Require(FeatureRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        Registrations.Requirements.Add(requirement);
        return this;
    }

    /// <inheritdoc />
    public ICoordinationBuilder Define(GuaranteeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Registrations.Definitions.Add(definition);
        return this;
    }

    /// <inheritdoc />
    public ICoordinationBuilder UseSingleNode()
    {
        Registrations.IsSingleNode = true;
        return this;
    }
}
