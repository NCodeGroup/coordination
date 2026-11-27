// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests.Conformance;

// The server's clock advances only by waiting, so the target's clock waits.
// The target drops only its own provider's connections, by client name, since every test shares the servers.
internal static class RespConformance
{
    /// <summary>
    /// Creates the target of one conformance test: a provider connected to the servers at an endpoint under a client
    /// name of its own, so the target's drop kills only its own connections, and a clock that advances by waiting.
    /// </summary>
    /// <param name="endpoint">The <c>host:port</c> list of the servers.</param>
    /// <param name="profile">The profile of the provider under test.</param>
    /// <param name="cancellationToken">The token that cancels the creation.</param>
    /// <returns>The target of the test.</returns>
    public static async ValueTask<ConformanceTarget> CreateTargetAsync(
        string endpoint,
        RespProfile profile,
        CancellationToken cancellationToken
    )
    {
        Assert.SkipUnless(
            RespServers.Enabled,
            $"Set {RespServers.EnvironmentVariable}=docker to run the tests against Valkey and Garnet in Docker."
        );

        var clientName = "conformance-" + Guid.NewGuid().ToString("N");

        var connection = new RespConnection(
            async ct =>
            {
                var options = ConfigurationOptions.Parse(endpoint);
                options.ClientName = clientName;
                var connecting = ConnectionMultiplexer.ConnectAsync(options);
                return await connecting.WaitAsync(ct);
            },
            owned: true
        );
        var provider = new RespProvider(connection, profile);

        try
        {
            await provider.ProbeAsync(cancellationToken);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        return new ConformanceTarget(provider)
        {
            AdvanceClock = WaitAsync,
            DropConnections = ct => DropConnectionsAsync(endpoint, clientName, ct),
        };
    }

    /// <summary>
    /// Kills every connection that the provider under test holds, on every node, so a test sees the provider recover
    /// from a dropped connection without disturbing the other tests that share the servers.
    /// </summary>
    /// <param name="endpoint">The <c>host:port</c> list of the servers.</param>
    /// <param name="clientName">The client name that only the provider under test uses.</param>
    /// <param name="cancellationToken">The token that cancels the drop.</param>
    /// <returns>A task that completes when the connections are killed.</returns>
    private static async ValueTask DropConnectionsAsync(
        string endpoint,
        string clientName,
        CancellationToken cancellationToken
    )
    {
        await using var admin = await RespNodes.ConnectAdminAsync(endpoint, cancellationToken);

        long killed = 0;
        foreach (var server in admin.GetServers())
        {
            var listing = server.ClientListAsync();
            var clients = await listing.WaitAsync(cancellationToken);
            foreach (var client in clients)
            {
                if (!string.Equals(client.Name, clientName, StringComparison.Ordinal))
                    continue;

                var killing = server.ClientKillAsync(client.Id, null, null, true);
                var count = await killing.WaitAsync(cancellationToken);
                killed += count;
            }
        }

        Assert.True(killed > 0, "The drop found no connection of the provider to kill.");
    }

    private static ValueTask WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var delay = Task.Delay(duration, cancellationToken);
        return new ValueTask(delay);
    }
}
