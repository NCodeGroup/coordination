// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// Starts Garnet cluster nodes in one container and joins them into a cluster from the tests, since the Garnet image
/// carries no command-line client that could join them inside the container.
/// </summary>
internal static class RespGarnetCluster
{
    /// <summary>
    /// The line that the container's script prints once every node accepts connections, which the container's wait
    /// strategy watches for.
    /// </summary>
    private const string NodesReadyMessage = "Garnet nodes ready";

    /// <summary>
    /// The number of hash slots that a cluster divides between its primaries.
    /// </summary>
    private const int SlotCount = 16_384;

    /// <summary>
    /// Creates the container that runs one Garnet cluster node per port, each published on the host as itself.
    /// </summary>
    /// <param name="ports">The host ports of the nodes.</param>
    /// <param name="nodeArguments">The arguments that every node takes besides the cluster's own.</param>
    /// <returns>The container, not yet started.</returns>
    public static IContainer CreateContainer(
        IReadOnlyList<int> ports,
        IReadOnlyList<string> nodeArguments
    )
    {
        var script = Script(ports, nodeArguments);
        var waitStrategy = Wait.ForUnixContainer().UntilMessageIsLogged(NodesReadyMessage);
        var builder = new ContainerBuilder(RespServers.GarnetImage)
            .WithEntrypoint("sh", "-c")
            .WithCommand(script)
            .WithWaitStrategy(waitStrategy);

        foreach (var port in ports)
        {
            builder = builder.WithPortBinding(port, port);
        }

        return builder.Build();
    }

    /// <summary>
    /// Gets the <c>host:port</c> list of the nodes, which the tests connect to.
    /// </summary>
    /// <param name="ports">The host ports of the nodes.</param>
    /// <returns>The comma-separated list of the nodes' addresses.</returns>
    public static string GetEndpoint(IReadOnlyList<int> ports)
    {
        var addresses = ports.Select(port => $"{RespNodes.Host}:{port}");
        return string.Join(",", addresses);
    }

    /// <summary>
    /// Joins started nodes into a cluster: the first nodes split the slots between them as primaries, and each later
    /// node replicates a primary, in turn.
    /// </summary>
    /// <param name="ports">The host ports of the nodes.</param>
    /// <param name="primaries">The number of nodes, from the first, that serve slots.</param>
    /// <param name="cancellationToken">The token that cancels the join.</param>
    /// <returns>
    /// A task that completes when every node knows the others, every slot is served and every replica's link is up.
    /// </returns>
    public static async ValueTask JoinAsync(
        IReadOnlyList<int> ports,
        int primaries,
        CancellationToken cancellationToken
    )
    {
        var endpoint = GetEndpoint(ports);
        await using var admin = await RespNodes.ConnectAdminAsync(endpoint, cancellationToken);
        var nodes = new List<IServer>(ports.Count);
        foreach (var port in ports)
        {
            var node = admin.GetServer(RespNodes.Host, port);
            nodes.Add(node);
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var epoch = index + 1;
            var configuring = nodes[index]
                .ExecuteAsync(RespNodes.ClusterCommand, "SET-CONFIG-EPOCH", epoch);
            await configuring.WaitAsync(cancellationToken);
        }

        for (var index = 0; index < primaries; index++)
        {
            var first = SlotCount * index / primaries;
            var last = (SlotCount * (index + 1) / primaries) - 1;
            var assigning = nodes[index]
                .ExecuteAsync(RespNodes.ClusterCommand, "ADDSLOTSRANGE", first, last);
            await assigning.WaitAsync(cancellationToken);
        }

        for (var index = 1; index < nodes.Count; index++)
        {
            var meeting = nodes[0]
                .ExecuteAsync(RespNodes.ClusterCommand, "MEET", RespNodes.Host, ports[index]);
            await meeting.WaitAsync(cancellationToken);
        }

        var knownNodes = $"cluster_known_nodes:{nodes.Count}";
        foreach (var node in nodes)
        {
            await RespNodes.WaitForReplyAsync(
                node,
                [RespNodes.ClusterCommand, "INFO"],
                reply =>
                    reply.Contains("cluster_state:ok", StringComparison.Ordinal)
                    && reply.Contains(knownNodes, StringComparison.Ordinal),
                cancellationToken
            );
        }

        for (var index = primaries; index < nodes.Count; index++)
        {
            var followed = nodes[(index - primaries) % primaries];
            var identifying = followed.ExecuteAsync(RespNodes.ClusterCommand, "MYID");
            var reply = await identifying.WaitAsync(cancellationToken);
            var id = reply.ToString();
            var replica = nodes[index];
            var replicating = replica.ExecuteAsync(RespNodes.ClusterCommand, "REPLICATE", id);
            await replicating.WaitAsync(cancellationToken);
            await RespNodes.WaitForReplicationAsync(
                replica,
                text => text.Contains(RespNodes.LinkUp, StringComparison.Ordinal),
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Builds the shell script that starts one Garnet cluster node per port in the background and prints the ready
    /// message once every node accepts connections.
    /// </summary>
    /// <param name="ports">The host ports of the nodes.</param>
    /// <param name="nodeArguments">The arguments that every node takes besides the cluster's own.</param>
    /// <returns>The script that the container runs.</returns>
    private static string Script(IReadOnlyList<int> ports, IReadOnlyList<string> nodeArguments)
    {
        var list = string.Join(" ", ports);
        IReadOnlyList<string> arguments = [.. RespServers.GarnetCommand, .. nodeArguments];
        var extra = string.Join(" ", arguments);
        return $"for p in {list}; do "
            + $"/app/GarnetServer --port $p --cluster --checkpointdir /data/$p --cluster-announce-ip {RespNodes.Host} {extra} "
            + "> /data/$p.log 2>&1 & done; "
            + $"for p in {list}; do until grep -q '{RespServers.ReadyMessage}' /data/$p.log; do sleep 0.1; done; done; "
            + $"echo '{NodesReadyMessage}'; wait";
    }
}
