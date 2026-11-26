// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.Hosting.Tests;

namespace NCode.Coordination.Hosting.Sessions.Tests;

public sealed class DefaultHostSessionServiceTests
{
    private static CoordinationKey LeaseKey { get; } = HostingTesting.Keyspace.CreateKey("lease");

    private static DefaultHostSessionService CreateHostService(
        IServiceProvider services,
        FakeTimeProvider time
    ) =>
        HostingTesting.CreateService(
            time,
            services.GetRequiredService<ISessionService>(),
            services.GetRequiredService<IMembershipService>()
        );

    private static async Task<SessionId> OpenSessionAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var sessions = services.GetRequiredService<ISessionService>();
        var timeToLive = new TimeToLive(TimeSpan.FromMinutes(1));
        var grant = await sessions.OpenAsync(
            HostingTesting.Keyspace,
            timeToLive,
            null,
            cancellationToken
        );
        return grant.Id;
    }

    #region Constructor Tests

    [Fact]
    public async Task Constructor_WhenTheOptionsAreInvalid_Throws()
    {
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        var sessions = services.GetRequiredService<ISessionService>();
        var membership = services.GetRequiredService<IMembershipService>();
        var options = HostingTesting.CreateOptions();
        options.NodeId = "";

        var exception = Assert.Throws<OptionsValidationException>(() =>
            new DefaultHostSessionService(
                sessions,
                membership,
                Options.Create(options),
                time,
                NullLogger<DefaultHostSessionService>.Instance
            )
        );

        var failure = Assert.Single(exception.Failures);
        Assert.StartsWith("NodeId: ", failure);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenTheProviderAnswers_HoldsASessionAndJoinsTheGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        await using var service = CreateHostService(services, time);
        var membership = services.GetRequiredService<IMembershipService>();

        await service.StartAsync(cancellationToken);

        var session = await service.WaitForSessionAsync(cancellationToken);
        var view = await membership.ReadAsync(HostingTesting.Group, null, cancellationToken);
        var member = Assert.Single(view.Members);
        Assert.Equal(HostSessionState.Active, service.State);
        Assert.Same(session, service.Current);
        Assert.Equal(session.Id, member.Session);
        Assert.Equal("node-1", member.NodeId);
        Assert.Equal("10.0.0.1:5000", member.Address);
        Assert.Equal("eu-1", member.Metadata["zone"]);
    }

    [Fact]
    public async Task StartAsync_WhenTheNodeIdIsTaken_OpensTheSessionOnceTheOtherEnds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        await using var service = CreateHostService(services, time);
        var sessions = services.GetRequiredService<ISessionService>();
        var membership = services.GetRequiredService<IMembershipService>();
        var other = await OpenSessionAsync(services, cancellationToken);
        var otherMember = new Member(other, "node-1", "10.0.0.2:5000");
        var joined = await membership.JoinAsync(
            HostingTesting.Group,
            otherMember,
            null,
            cancellationToken
        );

        await service.StartAsync(cancellationToken);

        Assert.Equal(MembershipJoinStatus.Joined, joined);
        Assert.Equal(HostSessionState.Opening, service.State);
        Assert.Null(service.Current);

        await sessions.CloseAsync(other, null, cancellationToken);
        time.Advance(TimeSpan.FromSeconds(2));

        var session = await service.WaitForSessionAsync(cancellationToken);
        var view = await membership.ReadAsync(HostingTesting.Group, null, cancellationToken);
        var member = Assert.Single(view.Members);
        Assert.Equal(session.Id, member.Session);
    }

    #endregion

    #region WaitForSessionAsync Tests

    [Fact]
    public async Task WaitForSessionAsync_BeforeTheHostStarts_WaitsForTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        await using var service = CreateHostService(services, time);

        var wait = service.WaitForSessionAsync(cancellationToken);

        Assert.False(wait.IsCompleted);

        await service.StartAsync(cancellationToken);

        var session = await wait;
        Assert.Same(service.Current, session);
    }

    [Fact]
    public async Task WaitForSessionAsync_WhenTheHostStops_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        await using var service = CreateHostService(services, time);
        var pending = service.WaitForSessionAsync(cancellationToken).AsTask();

        await service.StoppedAsync(cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.WaitForSessionAsync(cancellationToken)
        );
    }

    #endregion

    #region StoppedAsync Tests

    [Fact]
    public async Task StoppedAsync_WhenTheHostStops_ClosesTheSessionAndReleasesItsLease()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        await using var service = CreateHostService(services, time);
        var leases = services.GetRequiredService<ILeaseService>();
        var membership = services.GetRequiredService<IMembershipService>();
        await service.StartAsync(cancellationToken);
        var session = await service.WaitForSessionAsync(cancellationToken);
        var held = await leases.TryAcquireAsync(
            LeaseKey,
            LeaseTerms.ForSession(session.Id),
            null,
            cancellationToken
        );
        var other = await OpenSessionAsync(services, cancellationToken);

        await service.StoppedAsync(cancellationToken);

        var acquired = await leases.TryAcquireAsync(
            LeaseKey,
            LeaseTerms.ForSession(other),
            null,
            cancellationToken
        );
        var view = await membership.ReadAsync(HostingTesting.Group, null, cancellationToken);
        Assert.True(held.IsAcquired);
        Assert.True(session.Ended.IsCancellationRequested);
        Assert.Equal(HostSessionState.Stopped, service.State);
        Assert.Null(service.Current);
        Assert.True(acquired.IsAcquired);
        Assert.Empty(view.Members);
    }

    [Fact]
    public async Task StoppedAsync_WhenACallbackOnTheEndThrows_StillStops()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        await using var service = CreateHostService(services, time);
        var membership = services.GetRequiredService<IMembershipService>();
        await service.StartAsync(cancellationToken);
        var session = await service.WaitForSessionAsync(cancellationToken);
        session.Ended.Register(() => throw new InvalidOperationException("The callback failed."));

        await service.StoppedAsync(cancellationToken);

        var view = await membership.ReadAsync(HostingTesting.Group, null, cancellationToken);
        Assert.True(session.Ended.IsCancellationRequested);
        Assert.Equal(HostSessionState.Stopped, service.State);
        Assert.Empty(view.Members);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WhenASessionIsHeld_EndsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        var service = CreateHostService(services, time);
        await service.StartAsync(cancellationToken);
        var session = await service.WaitForSessionAsync(cancellationToken);

        await service.DisposeAsync();

        Assert.True(session.Ended.IsCancellationRequested);
        Assert.Null(service.Current);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task Dispose_WhenASessionIsHeld_EndsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        var service = CreateHostService(services, time);
        await service.StartAsync(cancellationToken);
        var session = await service.WaitForSessionAsync(cancellationToken);

        service.Dispose();

        Assert.True(session.Ended.IsCancellationRequested);
        Assert.Null(service.Current);
    }

    #endregion
}
