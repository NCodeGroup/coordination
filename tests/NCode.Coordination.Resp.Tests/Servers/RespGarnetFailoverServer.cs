// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Containers;

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// A Garnet cluster of a primary that serves every slot and its replica, in one container of one test's own, each node
/// on a host port reserved before it starts and published as itself.
/// </summary>
/// <param name="container">The container that runs both nodes.</param>
/// <param name="primaryPort">The host port of the primary.</param>
/// <param name="replicaPort">The host port of the replica.</param>
internal sealed class RespGarnetFailoverServer(
    IContainer container,
    int primaryPort,
    int replicaPort
) : IRespFailoverServer
{
    /// <summary>
    /// The number of fields of a <c>CLUSTER NODES</c> line before its slots.
    /// </summary>
    private const int NodeFields = 8;

    /// <summary>
    /// Gets the arguments that let a Garnet node replicate: a replica replays its primary's append-only file.
    /// </summary>
    private static IReadOnlyList<string> ReplicationArguments { get; } = ["--aof"];

    /// <summary>
    /// Starts the primary and its replica and joins them into a cluster.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started servers.</returns>
    public static async ValueTask<IRespFailoverServer> StartAsync(
        CancellationToken cancellationToken
    )
    {
        var ports = RespPorts.Reserve(2);
        var container = RespGarnetCluster.CreateContainer(ports, ReplicationArguments);
        try
        {
            await container.StartAsync(cancellationToken);
            await RespGarnetCluster.JoinAsync(ports, 1, cancellationToken);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }

        return new RespGarnetFailoverServer(container, ports[0], ports[1]);
    }

    /// <summary>
    /// Tells whether a node's <c>CLUSTER NODES</c> reply gives the node itself any slots.
    /// </summary>
    /// <param name="reply">The node's reply.</param>
    /// <returns><see langword="true" /> when the node serves slots.</returns>
    private static bool ServesSlots(string reply)
    {
        var lines = reply.Split('\n');
        var own = lines.First(line => line.Contains("myself", StringComparison.Ordinal));
        var trimmed = own.Trim();
        var fields = trimmed.Split(' ');
        return fields.Length > NodeFields;
    }

    /// <summary>
    /// Gets the container that runs both nodes.
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
    public string Endpoint => RespGarnetCluster.GetEndpoint([PrimaryPort, ReplicaPort]);

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
        var failing = replica.ExecuteAsync(RespNodes.ClusterCommand, "FAILOVER", "FORCE");
        await failing.WaitAsync(cancellationToken);
        await RespNodes.WaitForReplicationAsync(
            replica,
            reply => reply.Contains("role:master", StringComparison.Ordinal),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask DemotePrimaryAsync(CancellationToken cancellationToken)
    {
        await using var admin = await RespNodes.ConnectAdminAsync(Endpoint, cancellationToken);
        var primary = admin.GetServer(RespNodes.Host, PrimaryPort);
        await RespNodes.WaitForReplyAsync(
            primary,
            [RespNodes.ClusterCommand, "NODES"],
            reply => !ServesSlots(reply),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
