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
        var options = ConfigurationOptions.Parse(endpoint);
        options.AllowAdmin = true;
        await using var admin = await ConnectionMultiplexer
            .ConnectAsync(options)
            .WaitAsync(cancellationToken);

        long killed = 0;
        foreach (var server in admin.GetServers())
        {
            foreach (var client in await server.ClientListAsync())
            {
                if (!string.Equals(client.Name, clientName, StringComparison.Ordinal))
                    continue;

                killed += await server.ClientKillAsync(client.Id, null, null, true);
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
