// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.Locks.Tests;

public sealed class DefaultLockServiceTests
{
    private const int RacingCallers = 32;

    private static LeaseTerms Terms { get; } =
        LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)));

    private static CoordinationKey CreateResource() => RecipeTesting.Keyspace.CreateKey("resource");

    #region AcquireAsync Tests

    [Fact]
    public async Task AcquireAsync_WhenFree_ReturnsAGrant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var leases = services.GetRequiredService<ILeaseService>();
        var locks = new DefaultLockService(leases);
        var resource = CreateResource();

        var grant = await locks.AcquireAsync(resource, Terms, null, cancellationToken);

        Assert.NotNull(grant);
    }

    [Fact]
    public async Task AcquireAsync_WhenHeld_WaitsForTheRelease()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var leases = services.GetRequiredService<ILeaseService>();
        var locks = new DefaultLockService(leases);
        var resource = CreateResource();
        var held = await leases.TryAcquireAsync(resource, Terms, null, cancellationToken);
        var heldGrant = Assert.IsType<LeaseGrant>(held.Grant);

        var task = locks.AcquireAsync(resource, Terms, null, cancellationToken).AsTask();

        Assert.False(task.IsCompleted);

        var released = await leases.ReleaseAsync(heldGrant, null, cancellationToken);
        var grant = await task;

        Assert.True(released);
        Assert.NotNull(grant);
        Assert.True(grant.FencingToken > heldGrant.FencingToken);
    }

    [Fact]
    public async Task AcquireAsync_WhenTheSessionHasEnded_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var leases = services.GetRequiredService<ILeaseService>();
        var sessions = services.GetRequiredService<ISessionService>();
        var locks = new DefaultLockService(leases);
        var resource = CreateResource();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        await sessions.CloseAsync(session, null, cancellationToken);

        var grant = await locks.AcquireAsync(
            resource,
            LeaseTerms.ForSession(session),
            null,
            cancellationToken
        );

        Assert.Null(grant);
    }

    [Fact]
    public async Task AcquireAsync_WhenCallersRace_HoldsTheLockOneAtATime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var leases = services.GetRequiredService<ILeaseService>();
        var locks = new DefaultLockService(leases);
        var resource = CreateResource();
        var holders = new int[1];

        var tokens = await RecipeTesting.RunConcurrentlyAsync(
            RacingCallers,
            async _ =>
            {
                var grant = await locks.AcquireAsync(resource, Terms, null, cancellationToken);
                Assert.NotNull(grant);

                var concurrent = Interlocked.Increment(ref holders[0]);
                await Task.Yield();
                Interlocked.Decrement(ref holders[0]);

                var released = await leases.ReleaseAsync(grant, null, cancellationToken);
                Assert.True(released);
                Assert.Equal(1, concurrent);

                return grant.FencingToken.Value;
            },
            cancellationToken
        );

        Assert.Equal(RacingCallers, tokens.Distinct().Count());
    }

    #endregion
}
