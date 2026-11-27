// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Transactions;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Owns or borrows the Npgsql data source that every PostgreSQL connection of the provider comes from.
/// </summary>
internal sealed class PgSource : IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource _root;
    private readonly NpgsqlDataSource _primary;
    private readonly bool _owned;
    private int _disposed;

    private PgSource(NpgsqlDataSource root, NpgsqlDataSource primary, bool owned)
    {
        _root = root;
        _primary = primary;
        _owned = owned;
    }

    /// <summary>
    /// Builds a data source that the source owns, from a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>A source that disposes its data source when it is disposed.</returns>
    /// <exception cref="ArgumentException">The connection string is null or empty.</exception>
    public static PgSource FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        var connectionStringBuilder = builder.ConnectionStringBuilder;
        connectionStringBuilder.Enlist = false;
        if (connectionStringBuilder.KeepAlive == 0)
        {
            connectionStringBuilder.KeepAlive = 15;
        }

        if (connectionStringBuilder.Host?.Contains(',') == true)
        {
            var multiHost = builder.BuildMultiHost();
            var primary = multiHost.WithTargetSession(TargetSessionAttributes.Primary);
            return new PgSource(multiHost, primary, owned: true);
        }

        var dataSource = builder.Build();
        return new PgSource(dataSource, dataSource, owned: true);
    }

    /// <summary>
    /// Borrows the data source of the host, which the source never disposes.
    /// </summary>
    /// <param name="dataSource">The data source of the host.</param>
    /// <returns>A source that leaves the data source to its host.</returns>
    /// <exception cref="ArgumentNullException">The data source is null.</exception>
    public static PgSource FromDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var primary = dataSource is NpgsqlMultiHostDataSource multiHost
            ? multiHost.WithTargetSession(TargetSessionAttributes.Primary)
            : dataSource;
        return new PgSource(dataSource, primary, owned: false);
    }

    /// <summary>
    /// Gets the connection string of the data source.
    /// </summary>
    public string ConnectionString => _root.ConnectionString;

    /// <summary>
    /// Opens a connection to the primary, outside any ambient transaction.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the open.</param>
    /// <returns>An open connection that the caller disposes.</returns>
    public async ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        using var scope = new TransactionScope(
            TransactionScopeOption.Suppress,
            TransactionScopeAsyncFlowOption.Enabled
        );

        return await _primary.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _owned)
        {
            await _root.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _owned)
        {
            _root.Dispose();
        }
    }
}
