// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NCode.Coordination.PostgreSql.Tests.Servers;

/// <summary>
/// A PostgreSQL 19 server in Docker of one test's own, published on a host port reserved before it starts, so a stop
/// and a start keep its endpoint. Its data directory is on disk, not in memory, so a stop and a start keep its data.
/// </summary>
/// <param name="container">The container that runs the server.</param>
/// <param name="hostPort">The host port on which the container publishes the server's port.</param>
internal sealed class PgRestartableServer(IContainer container, int hostPort) : IAsyncDisposable
{
    /// <summary>
    /// The port on which PostgreSQL listens inside its container, to which the host port is published.
    /// </summary>
    private const ushort Port = 5432;

    /// <summary>
    /// The name of the superuser and of the database that the module creates.
    /// </summary>
    private const string UserName = "postgres";

    /// <summary>
    /// The password of the superuser, which the module sets.
    /// </summary>
    private const string Password = "postgres";

    /// <summary>
    /// Starts a server on a freshly reserved host port.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started server.</returns>
    public static async ValueTask<PgRestartableServer> StartAsync(
        CancellationToken cancellationToken
    )
    {
        return await PgPorts.StartOnFreePortsAsync(1, StartOnPortAsync, cancellationToken);
    }

    /// <summary>
    /// Starts a server on a host port.
    /// </summary>
    /// <param name="ports">The reserved host ports, of which the first publishes the server.</param>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started server.</returns>
    private static async ValueTask<PgRestartableServer> StartOnPortAsync(
        IReadOnlyList<int> ports,
        CancellationToken cancellationToken
    )
    {
        var hostPort = ports[0];
        var container = new PostgreSqlBuilder(PgServers.Image19)
            .WithCommand([.. PgServers.DurableCommand])
            .WithPortBinding(hostPort, Port)
            .Build();
        try
        {
            await container.StartAsync(cancellationToken);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }

        return new PgRestartableServer(container, hostPort);
    }

    /// <summary>
    /// Gets the container that runs the server, which a stop and a start keep.
    /// </summary>
    private IContainer Container { get; } = container;

    /// <summary>
    /// Gets the host port on which the container publishes the server, reserved before the first start so that every
    /// start uses it.
    /// </summary>
    private int HostPort { get; } = hostPort;

    /// <summary>
    /// Gets the connection string of the server as <c>postgres</c>, which a stop and a start keep, and which rechecks
    /// a host every second.
    /// </summary>
    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder
        {
            Host = Container.Hostname,
            Port = HostPort,
            Username = UserName,
            Password = Password,
            Database = UserName,
            HostRecheckSeconds = 1,
        }.ConnectionString;

    /// <summary>
    /// Stops the server.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the stop.</param>
    /// <returns>A task that completes when the server has stopped.</returns>
    public async ValueTask StopAsync(CancellationToken cancellationToken) =>
        await Container.StopAsync(cancellationToken);

    /// <summary>
    /// Starts the stopped server again on the same endpoint.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>A task that completes when the server accepts connections.</returns>
    public async ValueTask StartAgainAsync(CancellationToken cancellationToken) =>
        await Container.StartAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
