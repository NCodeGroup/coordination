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
    /// <summary>
    /// Gets the composed registrations, created on first use.
    /// </summary>
    private Lazy<ComposedCoordination> Composed { get; } =
        new(() => CoordinationComposer.Compose(registrations, services));

    /// <summary>
    /// Composes the registrations when that hasn't happened yet. A provider factory's exception is rethrown as it was
    /// thrown.
    /// </summary>
    /// <exception cref="CoordinationCompositionException">The composition is invalid.</exception>
    public void EnsureComposed() => _ = GetValid();

    /// <summary>
    /// Gets the composed registrations, composing them when that hasn't happened yet.
    /// </summary>
    /// <returns>The composition, when it is valid.</returns>
    /// <exception cref="CoordinationCompositionException">The composition is invalid.</exception>
    private ComposedCoordination GetValid()
    {
        var composed = Composed.Value;
        composed.FactoryFailure?.Throw();
        if (composed.Failures.Length > 0)
        {
            throw new CoordinationCompositionException(composed.Failures);
        }

        return composed;
    }

    /// <summary>
    /// Composes the registrations, then probes each provider in registration order.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the probes.</param>
    /// <returns>A task that completes when every probe has passed.</returns>
    /// <exception cref="CoordinationCompositionException">The composition is invalid; no provider is probed.</exception>
    public async ValueTask EnsureStartedAsync(CancellationToken cancellationToken)
    {
        var composed = GetValid();
        foreach (var provider in composed.Providers)
        {
            await provider.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the provider's implementation of a primitive for a keyspace.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <param name="primitive">The primitive.</param>
    /// <param name="keyspace">The keyspace of the key being used.</param>
    /// <returns>The bound provider's implementation.</returns>
    /// <exception cref="CoordinationException">No provider is bound to the primitive for the keyspace.</exception>
    /// <exception cref="CoordinationCompositionException">
    /// The composition is invalid; outside a host it is thrown on first use.
    /// </exception>
    public BoundService<TService> Resolve<TService>(Primitive primitive, Keyspace keyspace)
        where TService : class
    {
        var bindings = GetValid().Bindings;
        var found = bindings.Find(primitive, keyspace);
        if (found is { Service: TService service } bound)
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
            foreach (var provider in Composed.Value.Created)
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
            foreach (var provider in Composed.Value.Created)
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
