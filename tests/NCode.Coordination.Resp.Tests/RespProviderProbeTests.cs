// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Server;
using NCode.Coordination.Resp.Tests.Servers;
using StackExchange.Redis;

namespace NCode.Coordination.Resp.Tests;

public sealed class RespProviderProbeTests(RespServers servers)
{
    private static RespProvider CreateProvider(string endpoint, RespProfile profile)
    {
        var connection = new RespConnection(
            async ct =>
            {
                var connecting = ConnectionMultiplexer.ConnectAsync(endpoint);
                return await connecting.WaitAsync(ct);
            },
            owned: true
        );
        return new RespProvider(connection, profile);
    }

    #region ProbeAsync Tests

    [RespServerFact]
    public async Task ProbeAsync_ValkeyWithValkeyProfile_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.Valkey, RespProfiles.Valkey);

        await provider.ProbeAsync(cancellationToken);

        Assert.Equal("valkey", provider.Declaration.Profile);
    }

    [RespServerFact]
    public async Task ProbeAsync_GarnetWithGarnetProfile_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.Garnet, RespProfiles.Garnet);

        await provider.ProbeAsync(cancellationToken);

        Assert.Equal("garnet", provider.Declaration.Profile);
    }

    [RespServerFact]
    public async Task ProbeAsync_ValkeyWithGarnetProfile_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.Valkey, RespProfiles.Garnet);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Equal(
            "The RESP server is 'valkey', but the provider uses the 'garnet' profile.",
            exception.Message
        );
    }

    [RespServerFact]
    public async Task ProbeAsync_GarnetWithoutLua_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(servers.GarnetWithoutLua, RespProfiles.Garnet);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.StartsWith(
            "The RESP server refused the provider's probe script:",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [RespServerFact]
    public async Task ProbeAsync_UserWithoutEval_ThrowsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var admin = await ConnectionMultiplexer.ConnectAsync(servers.Valkey);
        await admin
            .GetDatabase()
            .ExecuteAsync(
                "ACL",
                "SETUSER",
                "probe-limited",
                "on",
                ">probe-limited-pass",
                "~*",
                "&*",
                "+@all",
                "-eval",
                "-evalsha"
            );
        await using var provider = CreateProvider(
            $"{servers.Valkey},user=probe-limited,password=probe-limited-pass",
            RespProfiles.Valkey
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await provider.ProbeAsync(cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.AccessDenied, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
