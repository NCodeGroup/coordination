// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition;

/// <summary>
/// Validates an application's registrations and composes them in one pass.
/// </summary>
internal static class CoordinationComposer
{
    /// <summary>
    /// Creates the providers, validates the definitions, declarations, bindings and requirements, and binds them.
    /// </summary>
    /// <param name="registrations">What the application registered.</param>
    /// <param name="services">The application's services, which each provider factory receives.</param>
    /// <returns>The providers and their bindings.</returns>
    /// <exception cref="CoordinationCompositionException">One or more registrations are invalid.</exception>
    public static ComposedCoordination Compose(
        CoordinationRegistrations registrations,
        IServiceProvider services
    )
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(services);

        var failures = new List<string>();
        var catalog = ProviderCatalog.Create(registrations.ProviderFactories, services, failures);

        var definitions = new List<GuaranteeDefinition>(Guarantees.Definitions);
        foreach (var provider in catalog.Providers)
        {
            definitions.AddRange(provider.Declaration.Definitions);
        }

        definitions.AddRange(registrations.Definitions);
        var checkedRegistry = GuaranteeRegistry.Create(definitions);
        var registry = checkedRegistry.Value;
        failures.AddRange(checkedRegistry.Failures);

        foreach (var provider in catalog.Providers)
        {
            var declaration = provider.Declaration;
            foreach (var support in declaration.Primitives)
            {
                if (!support.IsSupported)
                {
                    continue;
                }

                foreach (var guarantee in support.Guarantees)
                {
                    if (!registry.IsDefined(guarantee))
                    {
                        failures.Add(
                            $"The provider '{declaration.Provider}' declares the guarantee '{guarantee.Name}' on the primitive '{support.Primitive.Name}', which is not defined."
                        );
                    }
                }
            }
        }

        var checkedBound = PrimitiveBinder.Bind(registrations.Bindings, catalog, registry);
        failures.AddRange(checkedBound.Failures);
        var checkedTable = BindingTable.Create(checkedBound.Value);
        var table = checkedTable.Value;
        failures.AddRange(checkedTable.Failures);
        var requirementFailures = RequirementChecker.Check(
            registrations.Requirements,
            table,
            registry,
            registrations.IsSingleNode
        );
        failures.AddRange(requirementFailures);

        if (failures.Count > 0)
        {
            throw new CoordinationCompositionException(failures);
        }

        return new ComposedCoordination(catalog.Providers, table);
    }
}
