// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Composition;

/// <summary>
/// Turns the bindings an application declared into the primitives each provider serves.
/// </summary>
internal static class PrimitiveBinder
{
    /// <summary>
    /// Binds each primitive of each binding to its provider, adding a failure for each one that cannot be bound.
    /// </summary>
    /// <param name="bindings">The bindings, in registration order.</param>
    /// <param name="catalog">The registered providers.</param>
    /// <param name="registry">The guarantees the application can name.</param>
    /// <param name="failures">The list that receives each failure.</param>
    /// <returns>The bound primitives, in binding order.</returns>
    public static ImmutableArray<BoundPrimitive> Bind(
        IEnumerable<ProviderBinding> bindings,
        ProviderCatalog catalog,
        GuaranteeRegistry registry,
        ICollection<string> failures
    )
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(failures);

        var bound = ImmutableArray.CreateBuilder<BoundPrimitive>();
        foreach (var binding in bindings)
        {
            var name = binding.Provider;
            var provider = catalog.Find(name);
            if (provider is null)
            {
                failures.Add($"A binding names the provider '{name}', which is not registered.");
                continue;
            }

            var declaration = provider.Declaration;
            var primitives = binding.Primitives.IsEmpty
                ? [.. declaration.Primitives.Where(x => x.IsSupported).Select(x => x.Primitive)]
                : binding.Primitives;

            foreach (var primitive in primitives)
            {
                var support = declaration.Primitives.FirstOrDefault(x => x.Primitive == primitive);
                if (support is null)
                {
                    failures.Add(
                        $"The provider '{name}' is bound to the primitive '{primitive.Name}', which it does not list."
                    );
                    continue;
                }

                if (!support.IsSupported)
                {
                    failures.Add(
                        $"The provider '{name}' is bound to the primitive '{primitive.Name}', which it does not support: {support.UnsupportedReason}"
                    );
                    continue;
                }

                var type = PrimitiveServiceTypes.Find(primitive);
                object? service = null;
                if (type is not null)
                {
                    service = provider.GetPrimitiveService(type);
                    if (service is null || !type.IsInstanceOfType(service))
                    {
                        failures.Add(
                            $"The provider '{name}' supports the primitive '{primitive.Name}' but serves no {type.Name}."
                        );
                        continue;
                    }
                }

                var guarantees = registry.Close(support.Guarantees);
                bound.Add(
                    new BoundPrimitive(name, primitive, binding.Keyspace, service, guarantees)
                );
            }
        }

        return bound.ToImmutable();
    }
}
