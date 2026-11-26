// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NCode.Coordination.Hosting.Sessions;
using NCode.Coordination.Hosting.Tests;

namespace NCode.Coordination.Tests;

public sealed class CoordinationHostingServiceCollectionExtensionsTests
{
    private static ProviderBinding Binding { get; } =
        new(InProcessCoordinationBuilderExtensions.ProviderName);

    private static IHost BuildHost(
        Dictionary<string, string?> configuration,
        TimeProvider? time = null
    )
    {
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Configuration.AddInMemoryCollection(configuration);

        if (time is not null)
        {
            builder.Services.AddSingleton(time);
        }

        builder.Services.AddMetrics();
        builder.Services.AddCoordination(b =>
            b.AddInProcessProvider().Bind(Binding).UseSingleNode().RequireHostSession()
        );
        builder.Services.AddCoordinationHosting(
            builder.Configuration.GetSection(CoordinationHostOptions.SectionName)
        );
        builder.Services.AddHealthChecks().AddCoordinationCheck();
        return builder.Build();
    }

    #region AddCoordinationHosting Tests

    [Fact]
    public void AddCoordinationHosting_WhenTheActionIsNull_Throws()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            services.AddCoordinationHosting((Action<CoordinationHostOptions>)null!)
        );

        Assert.Equal("configure", exception.ParamName);
    }

    [Fact]
    public void AddCoordinationHosting_WhenTheConfigurationIsNull_Throws()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            services.AddCoordinationHosting((IConfiguration)null!)
        );

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void AddCoordinationHosting_WhenCalledTwice_RegistersOneHostedService()
    {
        var services = new ServiceCollection();

        services.AddCoordinationHosting(_ => { });
        services.AddCoordinationHosting(_ => { });

        Assert.Single(services, x => x.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task AddCoordinationHosting_WhenResolved_HostsTheSessionService()
    {
        var time = HostingTesting.CreateTime();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<TimeProvider>(time);
        services.AddCoordination(b => b.AddInProcessProvider().Bind(Binding));
        services.AddCoordinationHosting(options =>
        {
            options.Cluster = "hosting";
            options.NodeId = "node-1";
            options.Address = "10.0.0.1:5000";
        });
        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        var sessionServices = hostedServices.OfType<DefaultHostSessionService>();
        var hosted = Assert.Single(sessionServices);
        var sessions = provider.GetRequiredService<IHostSessionService>();

        Assert.Same(hosted, sessions);
    }

    [Fact]
    public async Task AddCoordinationHosting_WhenAConfigurationKeyIsUnknown_FailsTheStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                ["Coordination:Host:Clustr"] = "hosting",
                ["Coordination:Host:NodeId"] = "node-1",
                ["Coordination:Host:Address"] = "10.0.0.1:5000",
            }
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.StartAsync(cancellationToken)
        );

        var optionsType = typeof(CoordinationHostOptions).FullName;
        var expected = $"were not found on the instance of {optionsType}: 'Clustr'";
        Assert.EndsWith(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddCoordinationHosting_WhenAConfiguredValueIsInvalid_FailsTheStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                ["Coordination:Host:Cluster"] = "hosting",
                ["Coordination:Host:Address"] = "10.0.0.1:5000",
            }
        );

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            host.StartAsync(cancellationToken)
        );

        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("NodeId: ", failure);
    }

    [Fact]
    public async Task AddCoordinationHosting_WhenTheHostRuns_HoldsTheSessionAndLeavesAsItStops()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        using var host = BuildHost(
            new Dictionary<string, string?>
            {
                ["Coordination:Host:Cluster"] = "hosting",
                ["Coordination:Host:NodeId"] = "node-1",
                ["Coordination:Host:Address"] = "10.0.0.1:5000",
                ["Coordination:Host:Metadata:zone"] = "eu-1",
            },
            time
        );
        var sessions = host.Services.GetRequiredService<IHostSessionService>();
        var membership = host.Services.GetRequiredService<IMembershipService>();
        var health = host.Services.GetRequiredService<HealthCheckService>();

        await host.StartAsync(cancellationToken);

        var session = await sessions.WaitForSessionAsync(cancellationToken);
        var started = await membership.ReadAsync(HostingTesting.Group, null, cancellationToken);
        var member = Assert.Single(started.Members);
        Assert.Equal(session.Id, member.Session);
        Assert.Equal("eu-1", member.Metadata["zone"]);
        var running = await health.CheckHealthAsync(cancellationToken);
        Assert.Equal(HealthStatus.Healthy, running.Status);

        await host.StopAsync(cancellationToken);

        var stopped = await membership.ReadAsync(HostingTesting.Group, null, cancellationToken);
        Assert.Empty(stopped.Members);
        var left = await health.CheckHealthAsync(cancellationToken);
        Assert.Equal(HealthStatus.Unhealthy, left.Status);
    }

    #endregion
}
