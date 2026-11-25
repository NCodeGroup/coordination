// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace NCode.Coordination.Composition;

/// <summary>
/// The providers an application registered, created once and found by name.
/// </summary>
internal sealed class ProviderCatalog
{
    private ProviderCatalog(
        ImmutableArray<ICoordinationProvider> providers,
        FrozenDictionary<string, ICoordinationProvider> byName
    )
    {
        Providers = providers;
        ByName = byName;
    }

    /// <summary>
    /// Gets the accepted providers, in registration order.
    /// </summary>
    public ImmutableArray<ICoordinationProvider> Providers { get; }

    private FrozenDictionary<string, ICoordinationProvider> ByName { get; }

    /// <summary>
    /// Creates the providers from their factories, adding a failure for each one that cannot be accepted.
    /// </summary>
    /// <param name="factories">The provider factories, in registration order.</param>
    /// <param name="services">The application's services, which each factory receives.</param>
    /// <param name="failures">The list that receives each failure.</param>
    /// <returns>The catalog of the accepted providers.</returns>
    public static ProviderCatalog Create(
        IEnumerable<Func<IServiceProvider, ICoordinationProvider>> factories,
        IServiceProvider services,
        ICollection<string> failures
    )
    {
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(failures);

        var providers = ImmutableArray.CreateBuilder<ICoordinationProvider>();
        var byName = new Dictionary<string, ICoordinationProvider>(StringComparer.Ordinal);
        foreach (var factory in factories)
        {
            var provider = factory(services);
            if (provider is null)
            {
                failures.Add("A provider factory returned no provider.");
                continue;
            }

            var declaration = provider.Declaration;
            var name = declaration.Provider;
            if (!byName.TryAdd(name, provider))
            {
                failures.Add($"Two providers are named '{name}'.");
                continue;
            }

            providers.Add(provider);

            var listed = new HashSet<Primitive>();
            foreach (var support in declaration.Primitives)
            {
                if (!listed.Add(support.Primitive))
                {
                    failures.Add(
                        $"The provider '{name}' lists the primitive '{support.Primitive.Name}' more than once."
                    );
                }
            }
        }

        var accepted = providers.ToImmutable();
        var frozenByName = byName.ToFrozenDictionary();
        return new ProviderCatalog(accepted, frozenByName);
    }

    /// <summary>
    /// Finds a provider by the name its declaration gives.
    /// </summary>
    /// <param name="name">The provider's name.</param>
    /// <returns>The provider, or null when none has the name.</returns>
    public ICoordinationProvider? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByName.GetValueOrDefault(name);
    }
}
