// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessDeadlinesTests : IDisposable
{
    private static TimeSpan OneSecond { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan TwoSeconds { get; } = TimeSpan.FromSeconds(2);

    private static TimeSpan ThreeSeconds { get; } = TimeSpan.FromSeconds(3);

    private static TimeSpan FiveSeconds { get; } = TimeSpan.FromSeconds(5);

    private FakeTimeProvider Time { get; } = InProcessTesting.CreateTime();

    private Lock Gate { get; } = new();

    private InProcessDeadlines Deadlines { get; }

    public InProcessDeadlinesTests()
    {
        Deadlines = new InProcessDeadlines(Time, Gate);
    }

    public void Dispose()
    {
        Deadlines.Dispose();
    }

    private void RunDue()
    {
        using var scope = Gate.EnterScope();

        Deadlines.RunDue();
    }

    private void Cancel(InProcessDeadline? deadline)
    {
        using var scope = Gate.EnterScope();

        Deadlines.Cancel(deadline);
    }

    private InProcessExpiry Begin(TimeSpan timeToLive)
    {
        var limit = new TimeToLive(timeToLive);

        return InProcessExpiry.Begin(Time, limit);
    }

    private InProcessDeadline Add(InProcessExpiry expiry, Action expire)
    {
        using var scope = Gate.EnterScope();

        return Deadlines.Add(expiry, expire);
    }

    #region RunDue Tests

    [Fact]
    public void RunDue_BeforeTheDeadline_RunsNothing()
    {
        var runs = 0;
        var expiry = Begin(TwoSeconds);
        Add(expiry, () => runs++);

        Time.Advance(OneSecond);
        RunDue();

        Assert.Equal(0, runs);
        Assert.Equal(1, Deadlines.Pending);
    }

    [Fact]
    public void RunDue_AfterTheDeadline_RunsItOnce()
    {
        var runs = 0;
        var expiry = Begin(TwoSeconds);
        Add(expiry, () => runs++);

        Time.Advance(TwoSeconds);
        RunDue();
        RunDue();

        Assert.Equal(1, runs);
        Assert.Equal(0, Deadlines.Pending);
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Add_WhenTheDeadlinePasses_RunsItWithoutRunDue()
    {
        var runs = 0;
        var expiry = Begin(TwoSeconds);
        Add(expiry, () => runs++);

        Time.Advance(TwoSeconds);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Add_WhenTwoDeadlinesPass_RunsThemInDeadlineOrder()
    {
        List<int> order = [];
        var threeSecondExpiry = Begin(ThreeSeconds);
        Add(threeSecondExpiry, () => order.Add(3));
        var twoSecondExpiry = Begin(TwoSeconds);
        Add(twoSecondExpiry, () => order.Add(2));

        Time.Advance(FiveSeconds);

        Assert.Equal([2, 3], order);
    }

    [Fact]
    public void Add_WhenTheDeadlineIsBeyondTheLongestWait_RunsItWhenItComes()
    {
        var runs = 0;
        var threeDays = TimeSpan.FromDays(3);
        var twoDays = TimeSpan.FromDays(2);
        var oneDay = TimeSpan.FromDays(1);
        var expiry = Begin(threeDays);
        Add(expiry, () => runs++);

        Time.Advance(twoDays);
        Assert.Equal(0, runs);

        Time.Advance(oneDay);
        Assert.Equal(1, runs);
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public void Cancel_WhenCalledBeforeTheDeadline_KeepsItFromRunning()
    {
        var runs = 0;
        var expiry = Begin(TwoSeconds);
        var deadline = Add(expiry, () => runs++);

        Cancel(deadline);
        Time.Advance(ThreeSeconds);
        RunDue();

        Assert.Equal(0, runs);
        Assert.Equal(0, Deadlines.Pending);
    }

    [Fact]
    public void Cancel_WhenManyAreCancelled_CompactsTheQueue()
    {
        const int total = InProcessDeadlines.CompactFloor + 10;
        List<InProcessDeadline> added = [];
        for (var index = 0; index < total; index++)
        {
            var expiry = Begin(TwoSeconds);
            var deadline = Add(expiry, () => { });
            added.Add(deadline);
        }

        foreach (var deadline in added)
        {
            Cancel(deadline);
        }

        Assert.Equal(0, Deadlines.Pending);
        // The queue compacts at the first cancel that leaves it more than 2 * Pending + CompactFloor handles,
        // which is the 1,030th with 4 pending, and the later cancels leave it under that bound.
        Assert.Equal(4, Deadlines.Count);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WhenCalled_StopsTheTimer()
    {
        var runs = 0;
        var expiry = Begin(TwoSeconds);
        Add(expiry, () => runs++);

        Deadlines.Dispose();
        Time.Advance(ThreeSeconds);

        Assert.Equal(0, runs);
    }

    #endregion
}
