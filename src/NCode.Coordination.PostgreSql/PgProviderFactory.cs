// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NCode.Coordination.PostgreSql.Server;
using Npgsql;

namespace NCode.Coordination.PostgreSql;

/// <summary>
/// What the host's options settled, from which the coordination builder creates the PostgreSQL provider when the host
/// resolves it.
/// </summary>
/// <param name="ConnectionString">The connection string when the provider owns its data source, otherwise null.</param>
/// <param name="DataSourceFactory">The factory of the host's own data source when the host supplies one, otherwise null.</param>
/// <param name="Profile">The profile that the server must match.</param>
/// <param name="Settings">The settings of the provider.</param>
internal sealed record PgProviderFactory(
    string? ConnectionString,
    Func<IServiceProvider, NpgsqlDataSource>? DataSourceFactory,
    PostgreSqlProfile Profile,
    PgSettings Settings
)
{
    /// <summary>
    /// Creates the provider over the source that the options named.
    /// </summary>
    /// <param name="services">The services that the host's data source factory resolves from.</param>
    /// <returns>The provider.</returns>
    public PostgreSqlProvider Create(IServiceProvider services)
    {
        var source = CreateSource(services);
        var loggerFactory =
            services.GetService(typeof(ILoggerFactory)) as ILoggerFactory
            ?? NullLoggerFactory.Instance;

        return new PostgreSqlProvider(source, Profile, Settings, loggerFactory);
    }

    /// <summary>
    /// Creates the source of the provider's connections.
    /// </summary>
    /// <param name="services">The services that the host's data source factory resolves from.</param>
    /// <returns>A source that owns a data source built from the connection string, or one that wraps the host's data source.</returns>
    private PgSource CreateSource(IServiceProvider services)
    {
        if (DataSourceFactory is null)
        {
            return PgSource.FromConnectionString(ConnectionString ?? string.Empty);
        }

        var dataSource = DataSourceFactory(services);

        return PgSource.FromDataSource(dataSource);
    }
}
