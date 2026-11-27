// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// A RESP server in Docker of one test's own, published on a host port reserved before it starts, so a stop and a start
/// keep its endpoint.
/// </summary>
internal sealed class RespRestartableServer : IAsyncDisposable
{
    private const ushort Port = 6379;

    /// <summary>Starts a server from an image.</summary>
    /// <param name="image">The image to run.</param>
    /// <param name="command">The arguments of the server's command, empty to run the image's default.</param>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>The started server.</returns>
    public static async ValueTask<RespRestartableServer> StartAsync(
        string image,
        IReadOnlyList<string> command,
        CancellationToken cancellationToken
    )
    {
        var hostPort = RespPorts.Reserve(1)[0];
        var waitStrategy = Wait.ForUnixContainer().UntilMessageIsLogged(RespServers.ReadyMessage);
        var builder = new ContainerBuilder(image)
            .WithPortBinding(hostPort, Port)
            .WithWaitStrategy(waitStrategy);

        if (command.Count > 0)
        {
            builder = builder.WithCommand([.. command]);
        }

        var container = builder.Build();
        await container.StartAsync(cancellationToken);
        return new RespRestartableServer(container, hostPort);
    }

    private RespRestartableServer(IContainer container, int hostPort)
    {
        Container = container;
        HostPort = hostPort;
    }

    private IContainer Container { get; }

    private int HostPort { get; }

    /// <summary>Gets the <c>host:port</c> of the server, which a stop and a start keep.</summary>
    public string Endpoint => $"{Container.Hostname}:{HostPort}";

    /// <summary>Stops the server.</summary>
    /// <param name="cancellationToken">The token that cancels the stop.</param>
    /// <returns>A task that completes when the server has stopped.</returns>
    public async ValueTask StopAsync(CancellationToken cancellationToken) =>
        await Container.StopAsync(cancellationToken);

    /// <summary>Starts the stopped server again on the same endpoint.</summary>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>A task that completes when the server accepts connections.</returns>
    public async ValueTask StartAgainAsync(CancellationToken cancellationToken) =>
        await Container.StartAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
