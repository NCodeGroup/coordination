// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(NCode.Coordination.Resp.Tests.Servers.RespServers))]

namespace NCode.Coordination.Resp.Tests.Servers;

/// <summary>
/// Starts one Valkey and two Garnet servers in Docker for the whole test run, when
/// <see cref="EnvironmentVariable"/> is <c>docker</c>.
/// </summary>
public sealed class RespServers : IAsyncLifetime
{
    /// <summary>The environment variable that turns the server tests on when its value is <c>docker</c>.</summary>
    public const string EnvironmentVariable = "NCODE_COORDINATION_RESP_SERVERS";

    /// <summary>The Valkey image the tests run against.</summary>
    public const string ValkeyImage = "valkey/valkey:9.2.0";

    /// <summary>The Garnet image the tests run against.</summary>
    public const string GarnetImage = "ghcr.io/microsoft/garnet:2.2.13";

    private const string ReadyMessage = "Ready to accept connections";

    private const ushort Port = 6379;

    /// <summary>Gets a value indicating whether the server tests run.</summary>
    public static bool Enabled =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) == "docker";

    /// <summary>Gets the <c>host:port</c> of the Valkey server, empty until it is started.</summary>
    public string Valkey { get; private set; } = "";

    /// <summary>Gets the <c>host:port</c> of the Garnet server with Lua enabled, empty until it is started.</summary>
    public string Garnet { get; private set; } = "";

    /// <summary>Gets the <c>host:port</c> of the Garnet server with Lua disabled, empty until it is started.</summary>
    public string GarnetWithoutLua { get; private set; } = "";

    private IContainer? ValkeyContainer { get; set; }

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
        ValkeyContainer = valkey;
        GarnetContainer = garnet;
        GarnetWithoutLuaContainer = garnetWithoutLua;

        await Task.WhenAll(
            valkey.StartAsync(CancellationToken.None),
            garnet.StartAsync(CancellationToken.None),
            garnetWithoutLua.StartAsync(CancellationToken.None)
        );

        Valkey = GetEndpoint(valkey);
        Garnet = GetEndpoint(garnet);
        GarnetWithoutLua = GetEndpoint(garnetWithoutLua);
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
    }

    private static IContainer CreateContainer(string image, params string[] command)
    {
        var builder = new ContainerBuilder(image)
            .WithPortBinding(Port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged(ReadyMessage));

        if (command.Length > 0)
        {
            builder = builder.WithCommand(command);
        }

        return builder.Build();
    }

    private static string GetEndpoint(IContainer container) =>
        $"{container.Hostname}:{container.GetMappedPublicPort(Port)}";
}
