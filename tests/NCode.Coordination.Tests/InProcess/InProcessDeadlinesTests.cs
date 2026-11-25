// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessDeadlinesTests
{
    private static TimeSpan OneSecond { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan TwoSeconds { get; } = TimeSpan.FromSeconds(2);

    private static TimeSpan ThreeSeconds { get; } = TimeSpan.FromSeconds(3);

    private static TimeSpan FiveSeconds { get; } = TimeSpan.FromSeconds(5);

    private static InProcessExpiry Begin(TimeProvider time, TimeSpan timeToLive)
    {
        var limit = new TimeToLive(timeToLive);

        return InProcessExpiry.Begin(time, limit);
    }

    private static void RunDue(InProcessDeadlines deadlines, Lock gate)
    {
        using var scope = gate.EnterScope();

        deadlines.RunDue();
    }

    private static void Cancel(InProcessDeadlines deadlines, Lock gate, InProcessDeadline? deadline)
    {
        using var scope = gate.EnterScope();

        deadlines.Cancel(deadline);
    }

    private static InProcessDeadline Add(
        InProcessDeadlines deadlines,
        Lock gate,
        InProcessExpiry expiry,
        Action expire
    )
    {
        using var scope = gate.EnterScope();

        return deadlines.Add(expiry, expire);
    }

    #region RunDue Tests

    [Fact]
    public void RunDue_BeforeTheDeadline_RunsNothing()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        var expiry = Begin(time, TwoSeconds);
        Add(deadlines, gate, expiry, () => runs++);

        time.Advance(OneSecond);
        RunDue(deadlines, gate);

        Assert.Equal(0, runs);
        Assert.Equal(1, deadlines.Pending);
    }

    [Fact]
    public void RunDue_AfterTheDeadline_RunsItOnce()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        var expiry = Begin(time, TwoSeconds);
        Add(deadlines, gate, expiry, () => runs++);

        time.Advance(TwoSeconds);
        RunDue(deadlines, gate);
        RunDue(deadlines, gate);

        Assert.Equal(1, runs);
        Assert.Equal(0, deadlines.Pending);
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Add_WhenTheDeadlinePasses_RunsItWithoutRunDue()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        var expiry = Begin(time, TwoSeconds);
        Add(deadlines, gate, expiry, () => runs++);

        time.Advance(TwoSeconds);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Add_WhenTwoDeadlinesPass_RunsThemInDeadlineOrder()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        List<int> order = [];
        var threeSecondExpiry = Begin(time, ThreeSeconds);
        Add(deadlines, gate, threeSecondExpiry, () => order.Add(3));
        var twoSecondExpiry = Begin(time, TwoSeconds);
        Add(deadlines, gate, twoSecondExpiry, () => order.Add(2));

        time.Advance(FiveSeconds);

        Assert.Equal([2, 3], order);
    }

    [Fact]
    public void Add_WhenTheDeadlineIsBeyondTheLongestWait_RunsItWhenItComes()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        var threeDays = TimeSpan.FromDays(3);
        var twoDays = TimeSpan.FromDays(2);
        var oneDay = TimeSpan.FromDays(1);
        var expiry = Begin(time, threeDays);
        Add(deadlines, gate, expiry, () => runs++);

        time.Advance(twoDays);
        Assert.Equal(0, runs);

        time.Advance(oneDay);
        Assert.Equal(1, runs);
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public void Cancel_WhenCalledBeforeTheDeadline_KeepsItFromRunning()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        var expiry = Begin(time, TwoSeconds);
        var deadline = Add(deadlines, gate, expiry, () => runs++);

        Cancel(deadlines, gate, deadline);
        time.Advance(ThreeSeconds);
        RunDue(deadlines, gate);

        Assert.Equal(0, runs);
        Assert.Equal(0, deadlines.Pending);
    }

    [Fact]
    public void Cancel_WhenManyAreCancelled_CompactsTheQueue()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        const int total = InProcessDeadlines.CompactFloor + 10;
        List<InProcessDeadline> added = [];
        for (var index = 0; index < total; index++)
        {
            var expiry = Begin(time, TwoSeconds);
            var deadline = Add(deadlines, gate, expiry, () => { });
            added.Add(deadline);
        }

        foreach (var deadline in added)
        {
            Cancel(deadlines, gate, deadline);
        }

        Assert.Equal(0, deadlines.Pending);
        // The queue compacts at the first cancel that leaves it more than 2 * Pending + CompactFloor handles,
        // which is the 1,030th with 4 pending, and the later cancels leave it under that bound.
        Assert.Equal(4, deadlines.Count);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WhenCalled_StopsTheTimer()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        var expiry = Begin(time, TwoSeconds);
        Add(deadlines, gate, expiry, () => runs++);

        deadlines.Dispose();
        time.Advance(ThreeSeconds);

        Assert.Equal(0, runs);
    }

    #endregion
}
