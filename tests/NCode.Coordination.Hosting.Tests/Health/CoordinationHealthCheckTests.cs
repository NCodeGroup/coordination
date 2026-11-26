// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;

namespace NCode.Coordination.Hosting.Health.Tests;

public sealed class CoordinationHealthCheckTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private Mock<IHostSessionService> CreateSessions(HostSessionState state)
    {
        var sessions = Mocks.Create<IHostSessionService>();
        sessions.Setup(x => x.State).Returns(state).Verifiable();
        return sessions;
    }

    private static HealthCheckContext CreateContext(CoordinationHealthCheck check) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "coordination",
                check,
                HealthStatus.Degraded,
                null
            ),
        };

    #region CheckHealthAsync Tests

    [Fact]
    public async Task CheckHealthAsync_WhenTheSessionIsActive_IsHealthy()
    {
        var sessions = CreateSessions(HostSessionState.Active);
        var check = new CoordinationHealthCheck(sessions.Object);

        var result = await check.CheckHealthAsync(
            CreateContext(check),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("The host holds its coordination session.", result.Description);
    }

    [Theory]
    [InlineData(HostSessionState.NotStarted, "The host has not started its coordination session.")]
    [InlineData(HostSessionState.Opening, "The host is opening its coordination session.")]
    [InlineData(
        HostSessionState.Unreachable,
        "The coordination provider failed the host's last call."
    )]
    [InlineData(HostSessionState.Stopped, "The host has closed its coordination session.")]
    [InlineData((HostSessionState)99, "The host's coordination session is in the state 99.")]
    public async Task CheckHealthAsync_WhenNoSessionIsActive_ReportsTheFailureStatus(
        HostSessionState state,
        string description
    )
    {
        var sessions = CreateSessions(state);
        var check = new CoordinationHealthCheck(sessions.Object);

        var result = await check.CheckHealthAsync(
            CreateContext(check),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(description, result.Description);
    }

    #endregion

    #region AddCoordinationCheck Tests

    [Fact]
    public async Task AddCoordinationCheck_WhenCalled_RegistersAReadyCheck()
    {
        var sessions = CreateSessions(HostSessionState.Active);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sessions.Object);
        services.AddHealthChecks().AddCoordinationCheck();
        await using var serviceProvider = services.BuildServiceProvider();
        var healthChecks = serviceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthChecks.CheckHealthAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(report.Entries);
        Assert.Equal("coordination", entry.Key);
        Assert.Equal(HealthStatus.Healthy, entry.Value.Status);
        Assert.Equal(["ready"], entry.Value.Tags);
    }

    [Fact]
    public async Task AddCoordinationCheck_WithANameAndTags_UsesThem()
    {
        var sessions = CreateSessions(HostSessionState.Active);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sessions.Object);
        services.AddHealthChecks().AddCoordinationCheck("cluster", "startup", "cluster");
        await using var serviceProvider = services.BuildServiceProvider();
        var healthChecks = serviceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthChecks.CheckHealthAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(report.Entries);
        Assert.Equal("cluster", entry.Key);
        Assert.Contains("startup", entry.Value.Tags);
        Assert.Contains("cluster", entry.Value.Tags);
    }

    [Fact]
    public void AddCoordinationCheck_WhenTheNameIsEmpty_Throws()
    {
        var builder = new ServiceCollection().AddHealthChecks();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.AddCoordinationCheck(string.Empty)
        );

        Assert.Equal("name", exception.ParamName);
    }

    #endregion
}
