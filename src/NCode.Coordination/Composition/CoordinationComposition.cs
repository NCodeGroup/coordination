// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition;

/// <summary>
/// An application's composition: composed on first use, it resolves each primitive to its bound provider and owns the
/// providers it created.
/// </summary>
/// <param name="registrations">What the application registered.</param>
/// <param name="services">The application's services, from which providers are created.</param>
internal class CoordinationComposition(
    CoordinationRegistrations registrations,
    IServiceProvider services
) : IAsyncDisposable, IDisposable
{
    private Lazy<ComposedCoordination> Composed { get; } =
        new(() => CoordinationComposer.Compose(registrations, services));

    /// <summary>
    /// Composes the registrations when that hasn't happened yet.
    /// </summary>
    /// <exception cref="CoordinationCompositionException">The composition is invalid.</exception>
    public void EnsureComposed() => _ = Composed.Value;

    /// <summary>
    /// Resolves the provider's implementation of a primitive for a keyspace.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <param name="primitive">The primitive.</param>
    /// <param name="keyspace">The keyspace of the key being used.</param>
    /// <returns>The bound provider's implementation.</returns>
    /// <exception cref="CoordinationException">No provider is bound to the primitive for the keyspace.</exception>
    public BoundService<TService> Resolve<TService>(Primitive primitive, Keyspace keyspace)
        where TService : class
    {
        if (
            Composed.Value.Bindings.Find(primitive, keyspace) is { Service: TService service } bound
        )
        {
            return new BoundService<TService>(bound.Provider, primitive, service);
        }

        throw new CoordinationException(
            CoordinationErrorCodes.Unsupported,
            outcomeUnknown: false,
            $"No provider is bound to the primitive '{primitive}' for a keyspace of the cluster '{keyspace.Cluster}'."
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Composed.IsValueCreated)
        {
            foreach (var provider in Composed.Value.Providers)
            {
                if (provider is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Composed.IsValueCreated)
        {
            foreach (var provider in Composed.Value.Providers)
            {
                if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        GC.SuppressFinalize(this);
    }
}
