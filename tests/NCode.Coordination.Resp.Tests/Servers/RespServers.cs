// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(NCode.Coordination.Resp.Tests.Servers.RespServers))]

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// Starts one Valkey server, two Garnet servers and a three-node Valkey cluster in Docker for the whole test run, when
/// <see cref="EnvironmentVariable"/> is <c>docker</c>.
/// </summary>
public sealed class RespServers : IAsyncLifetime
{
    /// <summary>The environment variable that turns the server tests on when its value is <c>docker</c>.</summary>
    public const string EnvironmentVariable = "NCODE_COORDINATION_RESP_SERVERS";

    /// <summary>The Valkey image the tests run against, pinned by its tag and the digest of its multi-architecture index.</summary>
    public const string ValkeyImage =
        "valkey/valkey:9.2.0@sha256:1c2b25b09f354c66fe994733acdf12fcfd4bdf6f3bd92b88a93a70378d884056";

    /// <summary>The Garnet image the tests run against, pinned by its tag and the digest of its multi-architecture index.</summary>
    public const string GarnetImage =
        "ghcr.io/microsoft/garnet:2.2.13@sha256:a5b11ca8292400a6239437c34c96aaa515a55ab3613ef2e41a33914405f89442";

    /// <summary>
    /// The line that a Valkey or Garnet server logs when it accepts connections, which the container's wait strategy
    /// watches for.
    /// </summary>
    internal const string ReadyMessage = "Ready to accept connections";

    /// <summary>
    /// The line that the cluster script prints once every node is up and the cluster state is ok, which the cluster
    /// container's wait strategy watches for.
    /// </summary>
    private const string ClusterReadyMessage = "Cluster ready";

    /// <summary>
    /// The address that the cluster's nodes announce and that the cluster endpoint names, so the client sees each node
    /// once and can reach it from the host.
    /// </summary>
    private const string ClusterHost = "127.0.0.1";

    private const ushort Port = 6379;

    /// <summary>Gets a value indicating whether the server tests run.</summary>
    public static bool Enabled =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) == "docker";

    private static IContainer CreateContainer(string image, params IEnumerable<string> command)
    {
        string[] arguments = [.. command];
        var waitStrategy = Wait.ForUnixContainer().UntilMessageIsLogged(ReadyMessage);
        var builder = new ContainerBuilder(image)
            .WithPortBinding(Port, true)
            .WithWaitStrategy(waitStrategy);

        if (arguments.Length > 0)
        {
            builder = builder.WithCommand(arguments);
        }

        return builder.Build();
    }

    private static string GetEndpoint(IContainer container) =>
        $"{container.Hostname}:{container.GetMappedPublicPort(Port)}";

    /// <summary>
    /// Builds the shell script that starts one Valkey node per port, creates a cluster of them with no replicas, and
    /// prints the ready message once the cluster state is ok.
    /// </summary>
    /// <param name="ports">The host ports of the nodes, each published as itself.</param>
    /// <returns>The script that the cluster container runs.</returns>
    private static string ClusterScript(IReadOnlyList<int> ports)
    {
        var list = string.Join(" ", ports);
        var addresses = GetClusterAddresses(ports);
        var nodes = string.Join(" ", addresses);
        return "i=0; "
            + $"for p in {list}; do i=$((i+1)); "
            + "valkey-server --port $p --cluster-enabled yes --cluster-port $((16379+i)) "
            + $"--cluster-config-file nodes-$p.conf --cluster-announce-ip {ClusterHost} "
            + "--save '' --appendonly no --daemonize yes --logfile /data/$p.log; done; "
            + $"for p in {list}; do until valkey-cli -p $p ping >/dev/null 2>&1; do sleep 0.1; done; done; "
            + $"valkey-cli --cluster create {nodes} --cluster-replicas 0 --cluster-yes; "
            + $"for p in {list}; do until valkey-cli -p $p cluster info | grep -q cluster_state:ok; do sleep 0.1; done; done; "
            + $"echo '{ClusterReadyMessage}'; exec sleep infinity";
    }

    /// <summary>
    /// Gets the <c>host:port</c> of each node of the cluster.
    /// </summary>
    /// <param name="ports">The host ports of the nodes.</param>
    /// <returns>The address of each node, in the order of the ports.</returns>
    private static IReadOnlyList<string> GetClusterAddresses(IReadOnlyList<int> ports)
    {
        var addresses = ports.Select(port => $"{ClusterHost}:{port}");

        return [.. addresses];
    }

    /// <summary>Gets the <c>host:port</c> of the Valkey server, empty until it is started.</summary>
    public string Valkey { get; private set; } = "";

    /// <summary>Gets the <c>host:port</c> of the Garnet server with Lua enabled, empty until it is started.</summary>
    public string Garnet { get; private set; } = "";

    /// <summary>Gets the <c>host:port</c> of the Garnet server with Lua disabled, empty until it is started.</summary>
    public string GarnetWithoutLua { get; private set; } = "";

    /// <summary>
    /// Gets the <c>host:port</c> list of the Valkey cluster's three nodes, empty until it is started.
    /// </summary>
    public string ValkeyCluster { get; private set; } = "";

    private IContainer? ValkeyContainer { get; set; }

    /// <summary>
    /// Gets the container that runs the three nodes of the Valkey cluster, so that disposing it stops them all.
    /// </summary>
    private IContainer? ValkeyClusterContainer { get; set; }

    private IContainer? GarnetContainer { get; set; }

    private IContainer? GarnetWithoutLuaContainer { get; set; }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        var valkey = CreateContainer(ValkeyImage);
        var garnet = CreateContainer(GarnetImage, "--lua", "--lua-transaction-mode", "true");
        var garnetWithoutLua = CreateContainer(GarnetImage);

        var ports = RespPorts.Reserve(3);
        var script = ClusterScript(ports);
        var clusterWaitStrategy = Wait.ForUnixContainer().UntilMessageIsLogged(ClusterReadyMessage);
        var clusterBuilder = new ContainerBuilder(ValkeyImage)
            .WithEntrypoint("sh", "-c")
            .WithCommand(script)
            .WithWaitStrategy(clusterWaitStrategy);
        foreach (var port in ports)
        {
            clusterBuilder = clusterBuilder.WithPortBinding(port, port);
        }

        var valkeyCluster = clusterBuilder.Build();
        ValkeyContainer = valkey;
        GarnetContainer = garnet;
        GarnetWithoutLuaContainer = garnetWithoutLua;
        ValkeyClusterContainer = valkeyCluster;

        var startValkey = valkey.StartAsync(CancellationToken.None);
        var startGarnet = garnet.StartAsync(CancellationToken.None);
        var startGarnetWithoutLua = garnetWithoutLua.StartAsync(CancellationToken.None);
        var startValkeyCluster = valkeyCluster.StartAsync(CancellationToken.None);
        await Task.WhenAll(startValkey, startGarnet, startGarnetWithoutLua, startValkeyCluster);

        Valkey = GetEndpoint(valkey);
        Garnet = GetEndpoint(garnet);
        GarnetWithoutLua = GetEndpoint(garnetWithoutLua);
        var addresses = GetClusterAddresses(ports);
        ValkeyCluster = string.Join(",", addresses);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (ValkeyContainer is { } valkey)
        {
            await valkey.DisposeAsync();
        }

        if (GarnetContainer is { } garnet)
        {
            await garnet.DisposeAsync();
        }

        if (GarnetWithoutLuaContainer is { } garnetWithoutLua)
        {
            await garnetWithoutLua.DisposeAsync();
        }

        if (ValkeyClusterContainer is { } valkeyCluster)
        {
            await valkeyCluster.DisposeAsync();
        }
    }
}
