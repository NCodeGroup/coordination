// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessDeadlinesTests
{
    private static TimeSpan OneSecond { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan TwoSeconds { get; } = TimeSpan.FromSeconds(2);

    private static TimeSpan ThreeSeconds { get; } = TimeSpan.FromSeconds(3);

    private static TimeSpan FiveSeconds { get; } = TimeSpan.FromSeconds(5);

    private static InProcessExpiry Begin(TimeProvider time, TimeSpan timeToLive) =>
        InProcessExpiry.Begin(time, new TimeToLive(timeToLive));

    private static void RunDue(InProcessDeadlines deadlines, Lock gate)
    {
        using (gate.EnterScope())
        {
            deadlines.RunDue();
        }
    }

    private static void Cancel(InProcessDeadlines deadlines, Lock gate, InProcessDeadline? deadline)
    {
        using (gate.EnterScope())
        {
            deadlines.Cancel(deadline);
        }
    }

    private static InProcessDeadline Add(
        InProcessDeadlines deadlines,
        Lock gate,
        InProcessExpiry expiry,
        Action expire
    )
    {
        using (gate.EnterScope())
        {
            return deadlines.Add(expiry, expire);
        }
    }

    #region RunDue Tests

    [Fact]
    public void RunDue_BeforeTheDeadline_RunsNothing()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        Add(deadlines, gate, Begin(time, TwoSeconds), () => runs++);

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
        Add(deadlines, gate, Begin(time, TwoSeconds), () => runs++);

        time.Advance(TwoSeconds);
        RunDue(deadlines, gate);
        RunDue(deadlines, gate);

        Assert.Equal(1, runs);
        Assert.Equal(0, deadlines.Pending);
    }

    #endregion

    #region Timer Tests

    [Fact]
    public void Advance_WhenTheDeadlinePasses_RunsItWithoutRunDue()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        Add(deadlines, gate, Begin(time, TwoSeconds), () => runs++);

        time.Advance(TwoSeconds);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Advance_WhenTwoDeadlinesPass_RunsThemInDeadlineOrder()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        List<int> order = [];
        Add(deadlines, gate, Begin(time, ThreeSeconds), () => order.Add(3));
        Add(deadlines, gate, Begin(time, TwoSeconds), () => order.Add(2));

        time.Advance(FiveSeconds);

        Assert.Equal([2, 3], order);
    }

    [Fact]
    public void Advance_WhenTheDeadlineIsBeyondTheLongestWait_RunsItWhenItComes()
    {
        var time = InProcessTesting.CreateTime();
        var gate = new Lock();
        using var deadlines = new InProcessDeadlines(time, gate);
        var runs = 0;
        Add(deadlines, gate, Begin(time, TimeSpan.FromDays(3)), () => runs++);

        time.Advance(TimeSpan.FromDays(2));
        Assert.Equal(0, runs);

        time.Advance(TimeSpan.FromDays(1));
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
        var deadline = Add(deadlines, gate, Begin(time, TwoSeconds), () => runs++);

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
            added.Add(Add(deadlines, gate, Begin(time, TwoSeconds), () => { }));
        }

        foreach (var deadline in added)
        {
            Cancel(deadlines, gate, deadline);
        }

        Assert.Equal(0, deadlines.Pending);
        Assert.True(deadlines.Count < total);
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
        Add(deadlines, gate, Begin(time, TwoSeconds), () => runs++);

        deadlines.Dispose();
        time.Advance(ThreeSeconds);

        Assert.Equal(0, runs);
    }

    #endregion
}
