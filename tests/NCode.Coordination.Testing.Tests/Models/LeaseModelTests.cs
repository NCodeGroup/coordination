// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class LeaseModelTests
{
    private const string Holder = "holder";

    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Resource { get; } = new(Space, "lease");

    private static TimeSpan Lifetime { get; } = TimeSpan.FromSeconds(30);

    private static LeaseModel CreateModel() => new();

    private static LeaseCall Acquire { get; } = new(LeaseCall.Acquire, null, 0, Lifetime);

    private static LeaseCall Renew { get; } = new(LeaseCall.Renew, Holder, 5, Lifetime);

    private static LeaseCall Release { get; } = new(LeaseCall.Release, Holder, 5, Lifetime);

    private static LeaseState HeldState { get; } = new(Holder, 5, 5, TimeSpan.Zero, Lifetime);

    private static LeaseAcquireResult Acquired(long token)
    {
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(Lifetime));
        var grant = new LeaseGrant(
            Resource,
            new FencingToken(token),
            new OwnerToken(Holder),
            terms
        );
        return LeaseAcquireResult.Acquired(grant);
    }

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenAcquireIsAcquiredWhileFree_HoldsTheLease()
    {
        var model = CreateModel();
        var result = Acquired(5);
        var operation = HistoryTesting.Returned(Acquire, result);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState, next);
    }

    [Fact]
    public void TryApply_WhenAcquireTokenIsNotAboveTheFloor_Rejects()
    {
        var model = CreateModel();
        var result = Acquired(5);
        var operation = HistoryTesting.Returned(Acquire, result);
        var state = model.Initial with { Floor = 5 };

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAcquireIsHeldWhileHeld_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, LeaseAcquireResult.Held);

        var applied = model.TryApply(HeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState, next);
    }

    [Fact]
    public void TryApply_WhenAcquireIsHeldWhileFree_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, LeaseAcquireResult.Held);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAcquireIsAcquiredWhileHeld_Rejects()
    {
        var model = CreateModel();
        var result = Acquired(6);
        var operation = HistoryTesting.Returned(Acquire, result);

        var applied = model.TryApply(HeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAcquireReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, 1L);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewByTheHolderReturnsTrue_ExtendsTheHold()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, true);
        var state = HeldState with { Now = TimeSpan.FromSeconds(10) };

        var applied = model.TryApply(state, operation, out var next);

        var expected = state with { Until = TimeSpan.FromSeconds(40) };
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenRenewByTheHolderReturnsFalse_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, false);

        var applied = model.TryApply(HeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewIsStaleAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew with { Token = 4 }, false);

        var applied = model.TryApply(HeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState, next);
    }

    [Fact]
    public void TryApply_WhenRenewIsStaleAndReturnsTrue_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew with { Token = 4 }, true);

        var applied = model.TryApply(HeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, 1L);

        var applied = model.TryApply(HeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReleaseByTheHolderReturnsTrue_FreesTheLease()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Release, true);

        var applied = model.TryApply(HeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState with { Owner = null, Token = 0 }, next);
    }

    [Fact]
    public void TryApply_WhenReleaseIsStaleAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Release with { Owner = "other" }, false);

        var applied = model.TryApply(HeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState, next);
    }

    [Fact]
    public void TryApply_WhenReleaseIsStaleAndReturnsTrue_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Release with { Owner = "other" }, true);

        var applied = model.TryApply(HeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenClockAdvancesPastTheHold_AllowsAnotherAcquire()
    {
        var model = CreateModel();
        var advance = HistoryTesting.Returned(new ClockAdvance(TimeSpan.FromSeconds(31)), null);
        var result = Acquired(6);
        var acquire = HistoryTesting.Returned(Acquire, result);

        var advanced = model.TryApply(HeldState, advance, out var expired);
        Assert.NotNull(expired);
        var applied = model.TryApply(expired, acquire, out var next);

        Assert.True(advanced);
        Assert.True(applied);
        Assert.Equal(
            new LeaseState(Holder, 6, 6, TimeSpan.FromSeconds(31), TimeSpan.FromSeconds(61)),
            next
        );
    }

    [Fact]
    public void TryApply_WhenRenewComesAfterTheHoldEndsAndReturnsTrue_Rejects()
    {
        var model = CreateModel();
        var advance = HistoryTesting.Returned(new ClockAdvance(TimeSpan.FromSeconds(31)), null);
        var renew = HistoryTesting.Returned(Renew, true);

        var prepared = model.TryApply(HeldState, advance, out var expired);
        Assert.True(prepared);
        Assert.NotNull(expired);
        var applied = model.TryApply(expired, renew, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewComesAfterTheHoldEndsAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var advance = HistoryTesting.Returned(new ClockAdvance(TimeSpan.FromSeconds(31)), null);
        var renew = HistoryTesting.Returned(Renew, false);

        var prepared = model.TryApply(HeldState, advance, out var expired);
        Assert.True(prepared);
        Assert.NotNull(expired);
        var applied = model.TryApply(expired, renew, out var next);

        Assert.True(applied);
        Assert.Equal(expired, next);
    }

    [Fact]
    public void TryApply_WhenClockAdvances_AddsItToTheClock()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(new ClockAdvance(TimeSpan.FromSeconds(5)), null);

        var applied = model.TryApply(HeldState, operation, out var next);

        var expected = HeldState with { Now = TimeSpan.FromSeconds(5) };
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenAcquireIsIndeterminateWhileFree_HoldsTheLeaseForAnUnknownOwner()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Acquire);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new LeaseState(LeaseModel.UnknownOwner, 0, 0, TimeSpan.Zero, Lifetime), next);
    }

    [Fact]
    public void TryApply_WhenAcquireIsIndeterminateWhileHeld_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Acquire);

        var applied = model.TryApply(HeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState, next);
    }

    [Fact]
    public void TryApply_WhenRenewByTheHolderIsIndeterminate_ExtendsTheHold()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Renew);
        var state = HeldState with { Now = TimeSpan.FromSeconds(10) };

        var applied = model.TryApply(state, operation, out var next);

        var expected = state with { Until = TimeSpan.FromSeconds(40) };
        Assert.True(applied);
        Assert.Equal(expected, next);
    }

    [Fact]
    public void TryApply_WhenReleaseByTheHolderIsIndeterminate_FreesTheLease()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Release);

        var applied = model.TryApply(HeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState with { Owner = null, Token = 0 }, next);
    }

    [Fact]
    public void TryApply_WhenReleaseIsIndeterminateAndStale_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Release with { Token = 4 });

        var applied = model.TryApply(HeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(HeldState, next);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(new LeaseCall("other", null, 0, Lifetime), null);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var result = Acquired(5);
        var operation = HistoryTesting.Returned("acquire", result);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
