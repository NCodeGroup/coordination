// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql;

/// <summary>
/// The provider that keeps every primitive in the tables of one PostgreSQL schema, through Npgsql.
/// </summary>
internal sealed class PostgreSqlProvider : ICoordinationProvider, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Returns the primitives that the provider serves on a server of a profile.
    /// </summary>
    /// <param name="profile">The profile whose durability the provider declares.</param>
    /// <returns>The primitives, which are none until the services that serve them exist.</returns>
    internal static ImmutableArray<PrimitiveSupport> Declare(PostgreSqlProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return [];
    }

    /// <summary>
    /// Creates the provider and the services that share its data source.
    /// </summary>
    /// <param name="source">The source of the provider's connections. The provider disposes it, which disposes the data source only when the source owns it.</param>
    /// <param name="profile">The profile that the server must match.</param>
    /// <param name="settings">The settings of the provider.</param>
    /// <exception cref="ArgumentNullException">The source, the profile or the settings are null.</exception>
    public PostgreSqlProvider(PgSource source, PostgreSqlProfile profile, PgSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);

        Source = source;
        Profile = profile;
        Settings = settings;
        Server = new PgServer(source, settings);
        Keyspaces = new PgKeyspaces(Server, settings.Schema);
        Declaration = new ProviderDeclaration(
            PostgreSqlCoordinationBuilderExtensions.ProviderName,
            Declare(profile)
        )
        {
            Profile = profile.Name,
        };
    }

    /// <summary>
    /// Gets the source of the provider's connections.
    /// </summary>
    private PgSource Source { get; }

    /// <summary>
    /// Gets the profile that the server must match.
    /// </summary>
    private PostgreSqlProfile Profile { get; }

    /// <summary>
    /// Gets the settings of the provider.
    /// </summary>
    private PgSettings Settings { get; }

    /// <summary>
    /// Gets the seam between the provider and its server.
    /// </summary>
    private PgServer Server { get; }

    /// <summary>
    /// Gets the cache of the keyspaces' row ids.
    /// </summary>
    private PgKeyspaces Keyspaces { get; }

    /// <summary>
    /// Gets the primitive services by the interface that each implements.
    /// </summary>
    private FrozenDictionary<Type, object> Services { get; } = FrozenDictionary<Type, object>.Empty;

    /// <inheritdoc />
    public ProviderDeclaration Declaration { get; }

    /// <inheritdoc />
    public object? GetPrimitiveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return Services.GetValueOrDefault(serviceType);
    }

    /// <inheritdoc />
    public ValueTask ProbeAsync(CancellationToken cancellationToken) =>
        new PgProbe(Server, Source, Profile, Settings).RunAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Source.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => Source.Dispose();
}
