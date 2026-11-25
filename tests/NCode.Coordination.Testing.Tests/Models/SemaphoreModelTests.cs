// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class SemaphoreModelTests
{
    private const string First = "first";

    private const string Second = "second";

    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Resource { get; } = new(Space, "semaphore");

    private static TimeSpan Lifetime { get; } = TimeSpan.FromSeconds(30);

    private static SemaphoreModel CreateModel() => new();

    private static SemaphoreCall Acquire { get; } = new(SemaphoreCall.Acquire, 0, null, 0);

    private static SemaphoreCall Renew { get; } = new(SemaphoreCall.Renew, 0, First, 5);

    private static SemaphoreCall Release { get; } = new(SemaphoreCall.Release, 0, First, 5);

    private static SemaphoreState FirstHeldState { get; } = new(First, 5, 5, null, 0, 0, 0);

    private static SemaphoreState SecondHeldState { get; } = new(null, 0, 0, Second, 7, 7, 0);

    private static SemaphoreState BothHeldState { get; } = new(First, 5, 5, Second, 7, 7, 0);

    private static SemaphoreAcquireResult Acquired(int slot, long token, string owner) =>
        SemaphoreAcquireResult.Acquired(
            new SemaphoreGrant(
                Resource,
                slot,
                new FencingToken(token),
                new OwnerToken(owner),
                LeaseTerms.ForTimeToLive(new TimeToLive(Lifetime))
            )
        );

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenFirstSlotIsAcquiredWhileEmpty_HoldsTheFirstSlot()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, Acquired(0, 5, First));

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState, next);
    }

    [Fact]
    public void TryApply_WhenSecondSlotIsAcquiredWhileEmpty_HoldsTheSecondSlot()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, Acquired(1, 7, Second));

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(SecondHeldState, next);
    }

    [Fact]
    public void TryApply_WhenSecondSlotIsAcquiredAfterTheFirst_HoldsBothSlots()
    {
        var model = CreateModel();
        var first = HistoryTesting.Returned(Acquire, Acquired(0, 5, First));
        var second = HistoryTesting.Returned(Acquire, Acquired(1, 7, Second));

        model.TryApply(model.Initial, first, out var afterFirst);
        Assert.NotNull(afterFirst);
        var applied = model.TryApply(afterFirst, second, out var next);

        Assert.True(applied);
        Assert.Equal(BothHeldState, next);
    }

    [Fact]
    public void TryApply_WhenATakenSlotIsGrantedAgain_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, Acquired(0, 6, Second));

        var applied = model.TryApply(FirstHeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAcquireTokenIsNotAboveTheFloor_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, Acquired(0, 5, First));
        var state = model.Initial with { FirstFloor = 5 };

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAcquireSlotIsOutsideTheCapacity_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, Acquired(2, 5, First));

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAcquireIsAcquiredWhileBothAreHeld_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, Acquired(0, 9, First));

        var applied = model.TryApply(BothHeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAcquireIsFullWhileBothAreHeld_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, SemaphoreAcquireResult.Full);

        var applied = model.TryApply(BothHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(BothHeldState, next);
    }

    [Fact]
    public void TryApply_WhenAcquireIsFullWhileASlotIsFree_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Acquire, SemaphoreAcquireResult.Full);

        var applied = model.TryApply(FirstHeldState, operation, out _);

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
    public void TryApply_WhenRenewByTheHolderReturnsTrue_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, true);

        var applied = model.TryApply(FirstHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState, next);
    }

    [Fact]
    public void TryApply_WhenRenewByTheHolderReturnsFalse_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, false);

        var applied = model.TryApply(FirstHeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewIsStaleAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew with { Token = 4 }, false);

        var applied = model.TryApply(FirstHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState, next);
    }

    [Fact]
    public void TryApply_WhenRenewIsStaleAndReturnsTrue_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew with { Token = 4 }, true);

        var applied = model.TryApply(FirstHeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, 1L);

        var applied = model.TryApply(FirstHeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReleaseByTheHolderReturnsTrue_FreesTheSlotAndKeepsItsFloor()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Release, true);

        var applied = model.TryApply(BothHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(SecondHeldState with { FirstFloor = 5 }, next);
    }

    [Fact]
    public void TryApply_WhenReleaseByTheSecondHolderReturnsTrue_FreesTheSecondSlot()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(
            new SemaphoreCall(SemaphoreCall.Release, 1, Second, 7),
            true
        );

        var applied = model.TryApply(BothHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState with { SecondFloor = 7 }, next);
    }

    [Fact]
    public void TryApply_WhenReleaseIsStaleAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Release with { Owner = "other" }, false);

        var applied = model.TryApply(FirstHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState, next);
    }

    [Fact]
    public void TryApply_WhenReleaseIsStaleAndReturnsTrue_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Release with { Owner = "other" }, true);

        var applied = model.TryApply(FirstHeldState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReleaseComesAfterTheSlotIsFreedAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var freed = FirstHeldState with { FirstOwner = null, FirstToken = 0 };
        var operation = HistoryTesting.Returned(Release, false);

        var applied = model.TryApply(freed, operation, out var next);

        Assert.True(applied);
        Assert.Equal(freed, next);
    }

    [Fact]
    public void TryApply_WhenACallCarriesASlotOutsideTheCapacityAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Release with { Slot = 2 }, false);

        var applied = model.TryApply(FirstHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState, next);
    }

    [Fact]
    public void TryApply_WhenAcquireIsIndeterminateWhileEmpty_CountsAnUnknownHolder()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Acquire);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial with { Unknown = 1 }, next);
    }

    [Fact]
    public void TryApply_WhenAcquireIsIndeterminateWhileFull_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Acquire);

        var applied = model.TryApply(BothHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(BothHeldState, next);
    }

    [Fact]
    public void TryApply_WhenAnIndeterminateAcquireAndAHolderFillTheSemaphore_AcquireIsFull()
    {
        var model = CreateModel();
        var indeterminate = HistoryTesting.Indeterminate(Acquire);
        var full = HistoryTesting.Returned(Acquire, SemaphoreAcquireResult.Full);

        model.TryApply(FirstHeldState, indeterminate, out var state);
        Assert.NotNull(state);
        var applied = model.TryApply(state, full, out var next);

        Assert.True(applied);
        Assert.Equal(state, next);
    }

    [Fact]
    public void TryApply_WhenAnIndeterminateAcquireAndAHolderFillTheSemaphore_AcquireIsNotAcquired()
    {
        var model = CreateModel();
        var indeterminate = HistoryTesting.Indeterminate(Acquire);
        var acquire = HistoryTesting.Returned(Acquire, Acquired(1, 7, Second));

        model.TryApply(FirstHeldState, indeterminate, out var state);
        Assert.NotNull(state);
        var applied = model.TryApply(state, acquire, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewByTheHolderIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Renew);

        var applied = model.TryApply(FirstHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState, next);
    }

    [Fact]
    public void TryApply_WhenReleaseByTheHolderIsIndeterminate_FreesTheSlot()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Release);

        var applied = model.TryApply(FirstHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial with { FirstFloor = 5 }, next);
    }

    [Fact]
    public void TryApply_WhenReleaseIsIndeterminateAndStale_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Release with { Token = 4 });

        var applied = model.TryApply(FirstHeldState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(FirstHeldState, next);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(new SemaphoreCall("other", 0, null, 0), null);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned("acquire", Acquired(0, 5, First));

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
