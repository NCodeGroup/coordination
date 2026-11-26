// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Coordination.Hosting.Tests;

namespace NCode.Coordination.Hosting.Sessions.Tests;

public sealed class DefaultHostSessionServiceTests : IDisposable
{
    private static CoordinationKey LeaseKey { get; } = HostingTesting.Keyspace.CreateKey("lease");

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private static SessionGrant CreateGrant(string token) =>
        new(
            new SessionId(HostingTesting.Keyspace, token),
            new TimeToLive(TimeSpan.FromSeconds(30))
        );

    private static CoordinationException CreateUnavailable() =>
        new(CoordinationErrorCodes.Unavailable, true, "The provider is unavailable.");

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

    [Fact]
    public async Task StartAsync_WhenTheSessionIsRenewed_ExtendsItsDeadline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var grant = CreateGrant("s1");
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<KeepAliveResult>();
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .Setup(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        sessions
            .Setup(x =>
                x.KeepAliveAsync(
                    grant.Id,
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => called.TrySetResult())
            .Returns(new ValueTask<KeepAliveResult>(gate.Task))
            .Verifiable();
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MembershipJoinStatus.Joined)
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );

        await service.StartAsync(cancellationToken);
        var session = await service.WaitForSessionAsync(cancellationToken);
        time.Advance(TimeSpan.FromSeconds(10));
        await called.Task.WaitAsync(cancellationToken);
        gate.SetResult(KeepAliveResult.Renewed(new TimeToLive(TimeSpan.FromSeconds(30))));
        time.Advance(TimeSpan.FromSeconds(20));

        Assert.False(session.Ended.IsCancellationRequested);
        Assert.Same(session, service.Current);
        Assert.Equal(HostSessionState.Active, service.State);
    }

    [Fact]
    public async Task StartAsync_WhenRenewalsFailPastTheDeadline_EndsTheSessionAndOpensAnother()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var first = CreateGrant("s1");
        var second = CreateGrant("s2");
        var unavailable = new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            true,
            "The provider is unavailable."
        );
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .SetupSequence(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(first)
            .ReturnsAsync(second);
        sessions
            .Setup(x =>
                x.KeepAliveAsync(
                    first.Id,
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(unavailable)
            .Verifiable();
        sessions
            .Setup(x =>
                x.CloseAsync(first.Id, It.IsAny<OperationOptions?>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(unavailable)
            .Verifiable();
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MembershipJoinStatus.Joined)
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );

        await service.StartAsync(cancellationToken);
        var session = await service.WaitForSessionAsync(cancellationToken);
        time.Advance(TimeSpan.FromSeconds(28));

        Assert.Equal(first.Id, session.Id);
        Assert.True(session.Ended.IsCancellationRequested);

        var reopened = await service.WaitForSessionAsync(cancellationToken);

        Assert.Equal(second.Id, reopened.Id);
        sessions.Verify(
            x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task StartAsync_WhenTheProviderEndsTheSession_OpensAnother()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        await using var services = HostingTesting.BuildCoordination(time);
        await using var service = CreateHostService(services, time);
        var sessions = services.GetRequiredService<ISessionService>();
        var membership = services.GetRequiredService<IMembershipService>();
        await service.StartAsync(cancellationToken);
        var first = await service.WaitForSessionAsync(cancellationToken);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Ended.Register(() => ended.TrySetResult());

        await sessions.CloseAsync(first.Id, null, cancellationToken);
        time.Advance(TimeSpan.FromSeconds(10));
        await ended.Task.WaitAsync(cancellationToken);

        var second = await service.WaitForSessionAsync(cancellationToken);
        var view = await membership.ReadAsync(HostingTesting.Group, null, cancellationToken);
        var member = Assert.Single(view.Members);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, member.Session);
    }

    [Fact]
    public async Task StartAsync_WhenOpeningFails_ReportsUnreachableAndRetries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var grant = CreateGrant("s1");
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .SetupSequence(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(CreateUnavailable())
            .ReturnsAsync(grant);
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MembershipJoinStatus.Joined)
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );

        await service.StartAsync(cancellationToken);

        Assert.Equal(HostSessionState.Unreachable, service.State);
        Assert.Null(service.Current);

        time.Advance(TimeSpan.FromSeconds(2));

        var session = await service.WaitForSessionAsync(cancellationToken);
        Assert.Equal(grant.Id, session.Id);
        Assert.Equal(HostSessionState.Active, service.State);
        sessions.Verify(
            x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task StartAsync_WhenJoiningFails_ClosesTheSessionAndReportsUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var grant = CreateGrant("s1");
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .Setup(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        sessions
            .Setup(x =>
                x.CloseAsync(grant.Id, It.IsAny<OperationOptions?>(), It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(CreateUnavailable())
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );

        await service.StartAsync(cancellationToken);

        Assert.Equal(HostSessionState.Unreachable, service.State);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task StartAsync_WhenTheSessionEndsBeforeItJoins_KeepsOpening()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var grant = CreateGrant("s1");
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .Setup(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MembershipJoinStatus.SessionEnded)
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );

        await service.StartAsync(cancellationToken);

        Assert.Equal(HostSessionState.Opening, service.State);
        Assert.Null(service.Current);
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

    [Fact]
    public async Task StoppedAsync_WhenCalledTwice_ClosesTheSessionOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var grant = CreateGrant("s1");
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .Setup(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        sessions
            .Setup(x =>
                x.CloseAsync(grant.Id, It.IsAny<OperationOptions?>(), It.IsAny<CancellationToken>())
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MembershipJoinStatus.Joined)
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );
        await service.StartAsync(cancellationToken);

        var first = service.StoppedAsync(cancellationToken);
        var second = service.StoppedAsync(cancellationToken);
        await Task.WhenAll(first, second);

        Assert.Equal(HostSessionState.Stopped, service.State);
        sessions.Verify(
            x =>
                x.CloseAsync(
                    grant.Id,
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once()
        );
    }

    [Fact]
    public async Task StoppedAsync_WhenTheShutdownTimeoutPasses_Returns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var grant = CreateGrant("s1");
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .Setup(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        sessions
            .Setup(x =>
                x.CloseAsync(grant.Id, It.IsAny<OperationOptions?>(), It.IsAny<CancellationToken>())
            )
            .Returns(
                (SessionId _, OperationOptions? _, CancellationToken token) =>
                {
                    closing.TrySetResult();
                    return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, token));
                }
            )
            .Verifiable();
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MembershipJoinStatus.Joined)
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await service.StartAsync(cancellationToken);

        var stop = service.StoppedAsync(shutdown.Token);
        await closing.Task.WaitAsync(cancellationToken);

        Assert.False(stop.IsCompleted);

        await shutdown.CancelAsync();
        await stop;

        Assert.Equal(HostSessionState.Stopped, service.State);
    }

    [Fact]
    public async Task StoppedAsync_WhenClosingFails_Returns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = HostingTesting.CreateTime();
        var grant = CreateGrant("s1");
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        sessions
            .Setup(x =>
                x.OpenAsync(
                    It.IsAny<Keyspace>(),
                    It.IsAny<TimeToLive>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(grant)
            .Verifiable();
        sessions
            .Setup(x =>
                x.CloseAsync(grant.Id, It.IsAny<OperationOptions?>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(CreateUnavailable())
            .Verifiable();
        membership
            .Setup(x =>
                x.JoinAsync(
                    It.IsAny<CoordinationKey>(),
                    It.IsAny<Member>(),
                    It.IsAny<OperationOptions?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MembershipJoinStatus.Joined)
            .Verifiable();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );
        await service.StartAsync(cancellationToken);

        await service.StoppedAsync(cancellationToken);

        Assert.Equal(HostSessionState.Stopped, service.State);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task StoppedAsync_WhenTheSessionLoopFaulted_StillStops()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ThrowingTimerTimeProvider(HostingTesting.Start);
        var sessions = Mocks.Create<ISessionService>();
        var membership = Mocks.Create<IMembershipService>();
        await using var service = HostingTesting.CreateService(
            time,
            sessions.Object,
            membership.Object
        );
        await service.StartAsync(cancellationToken);
        Assert.Equal(HostSessionState.Unreachable, service.State);
        Assert.Null(service.Current);

        await service.StoppedAsync(cancellationToken);

        Assert.Equal(HostSessionState.Stopped, service.State);
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

    private sealed class ThrowingTimerTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        ) => throw new InvalidOperationException("The clock is broken.");
    }
}
