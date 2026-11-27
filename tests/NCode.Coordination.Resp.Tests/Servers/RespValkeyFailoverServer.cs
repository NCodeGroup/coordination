// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// A Valkey primary and its replica in one container of one test's own, each on a host port reserved before it starts
/// and published as itself.
/// </summary>
/// <param name="container">The container that runs both servers.</param>
/// <param name="primaryPort">The host port of the primary.</param>
/// <param name="replicaPort">The host port of the replica.</param>
internal sealed class RespValkeyFailoverServer(
    IContainer container,
    int primaryPort,
    int replicaPort
) : IRespFailoverServer
{
    /// <summary>
    /// The line that the container's script prints once the replica's link to the primary is up, which the container's
    /// wait strategy watches for.
    /// </summary>
    private const string ReplicationReadyMessage = "Replication ready";

    /// <summary>
    /// Starts a Valkey primary and a replica of it, neither of which persists anything.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started servers.</returns>
    public static async ValueTask<IRespFailoverServer> StartAsync(
        CancellationToken cancellationToken
    )
    {
        var ports = RespPorts.Reserve(2);
        var primaryPort = ports[0];
        var replicaPort = ports[1];
        var script = Script(primaryPort, replicaPort);
        var waitStrategy = Wait.ForUnixContainer().UntilMessageIsLogged(ReplicationReadyMessage);
        var container = new ContainerBuilder(RespServers.ValkeyImage)
            .WithEntrypoint("sh", "-c")
            .WithCommand(script)
            .WithPortBinding(primaryPort, primaryPort)
            .WithPortBinding(replicaPort, replicaPort)
            .WithWaitStrategy(waitStrategy)
            .Build();

        await container.StartAsync(cancellationToken);
        return new RespValkeyFailoverServer(container, primaryPort, replicaPort);
    }

    /// <summary>
    /// Builds the shell script that starts the primary and a replica of it in the background and prints the ready
    /// message once the replica's link is up.
    /// </summary>
    /// <param name="primaryPort">The host port of the primary.</param>
    /// <param name="replicaPort">The host port of the replica.</param>
    /// <returns>The script that the container runs.</returns>
    private static string Script(int primaryPort, int replicaPort)
    {
        const string options =
            "--save '' --appendonly no --repl-diskless-sync-delay 0 --daemonize yes";
        return $"valkey-server --port {primaryPort} {options} --logfile /data/{primaryPort}.log; "
            + $"valkey-server --port {replicaPort} --replicaof {RespNodes.Host} {primaryPort} {options} --logfile /data/{replicaPort}.log; "
            + $"until valkey-cli -p {replicaPort} info replication | grep -q {RespNodes.LinkUp}; do sleep 0.1; done; "
            + $"echo '{ReplicationReadyMessage}'; exec sleep infinity";
    }

    /// <summary>
    /// Gets the container that runs both servers.
    /// </summary>
    private IContainer Container { get; } = container;

    /// <summary>
    /// Gets the host port of the primary.
    /// </summary>
    private int PrimaryPort { get; } = primaryPort;

    /// <summary>
    /// Gets the host port of the replica.
    /// </summary>
    private int ReplicaPort { get; } = replicaPort;

    /// <inheritdoc />
    public string Endpoint => $"{RespNodes.Host}:{PrimaryPort},{RespNodes.Host}:{ReplicaPort}";

    /// <inheritdoc />
    public async ValueTask WaitForReplicaAsync(CancellationToken cancellationToken)
    {
        await using var admin = await RespNodes.ConnectAdminAsync(Endpoint, cancellationToken);
        var primary = admin.GetServer(RespNodes.Host, PrimaryPort);
        var replica = admin.GetServer(RespNodes.Host, ReplicaPort);
        await RespNodes.WaitForReplicaAsync(primary, replica, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask PromoteReplicaAsync(CancellationToken cancellationToken)
    {
        await using var admin = await RespNodes.ConnectAdminAsync(Endpoint, cancellationToken);
        var replica = admin.GetServer(RespNodes.Host, ReplicaPort);
        await replica.ExecuteAsync("REPLICAOF", "NO", "ONE");
    }

    /// <inheritdoc />
    public async ValueTask DemotePrimaryAsync(CancellationToken cancellationToken)
    {
        await using var admin = await RespNodes.ConnectAdminAsync(Endpoint, cancellationToken);
        var primary = admin.GetServer(RespNodes.Host, PrimaryPort);
        await primary.ExecuteAsync("REPLICAOF", RespNodes.Host, ReplicaPort);
        await RespNodes.WaitForReplicationAsync(
            primary,
            reply => reply.Contains(RespNodes.LinkUp, StringComparison.Ordinal),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
