// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests.Conformance;

// The server's clock advances only by waiting, so the target's clock waits.
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

        var connection = new RespConnection(
            async ct =>
            {
                var connecting = ConnectionMultiplexer.ConnectAsync(endpoint);
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

        return new ConformanceTarget(provider) { AdvanceClock = WaitAsync };
    }

    private static ValueTask WaitAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        new(Task.Delay(duration, cancellationToken));
}
