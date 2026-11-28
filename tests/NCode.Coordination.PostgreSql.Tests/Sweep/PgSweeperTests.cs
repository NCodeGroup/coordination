// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.PostgreSql.Logging;

namespace NCode.Coordination.PostgreSql.Sweep.Tests;

public sealed class PgSweeperTests
{
    private static TimeSpan Interval { get; } = TimeSpan.FromSeconds(5);

    private static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(10);

    private sealed class CountingTimeProvider : FakeTimeProvider
    {
        private int _timers;

        public int Timers => Volatile.Read(ref _timers);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            Interlocked.Increment(ref _timers);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class CountingSweep(bool failFirst)
    {
        private TaskCompletionSource First { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource Second { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task FirstCall => First.Task;

        public Task SecondCall => Second.Task;

        public ValueTask SweepAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                First.TrySetResult();
                if (!failFirst)
                    return ValueTask.CompletedTask;

                var failure = new InvalidOperationException();
                return ValueTask.FromException(failure);
            }

            Second.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private static Task WithSweeperAsync(
        bool failFirst,
        Func<PgSweeper, CountingTimeProvider, CountingSweep, Task> test
    )
    {
        var logger = NullLogger<PgSweeper>.Instance;
        return WithSweeperAsync(failFirst, logger, test);
    }

    private static async Task WithSweeperAsync(
        bool failFirst,
        ILogger<PgSweeper> logger,
        Func<PgSweeper, CountingTimeProvider, CountingSweep, Task> test
    )
    {
        var sweep = new CountingSweep(failFirst);
        var time = new CountingTimeProvider();

        await using var sweeper = new PgSweeper(sweep.SweepAsync, Interval, time, logger);
        await test(sweeper, time, sweep);
    }

    #region Start Tests

    [Fact]
    public async Task Start_Twice_StartsOneLoop()
    {
        await WithSweeperAsync(failFirst: false, (sweeper, time, _) =>
        {
            sweeper.Start();
            sweeper.Start();

            Assert.Equal(1, time.Timers);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Start_AfterDispose_StartsNoLoop()
    {
        await WithSweeperAsync(failFirst: false, async (sweeper, time, _) =>
        {
            await sweeper.DisposeAsync();

            sweeper.Start();

            Assert.Equal(0, time.Timers);
        });
    }

    [Fact]
    public async Task Start_IntervalPassed_Sweeps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweeperAsync(failFirst: false, async (sweeper, time, sweep) =>
        {
            sweeper.Start();

            time.Advance(Interval);

            await sweep.FirstCall.WaitAsync(WaitLimit, cancellationToken);
            Assert.Equal(1, sweep.Calls);
        });
    }

    [Fact]
    public async Task Start_SweepThrowsUnexpectedly_SweepsAgainAtTheNextTick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweeperAsync(failFirst: true, async (sweeper, time, sweep) =>
        {
            sweeper.Start();
            time.Advance(Interval);
            await sweep.FirstCall.WaitAsync(WaitLimit, cancellationToken);

            time.Advance(Interval);

            await sweep.SecondCall.WaitAsync(WaitLimit, cancellationToken);
        });
    }

    [Fact]
    public async Task Start_SweepThrowsUnexpectedly_LogsSweepFailedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logger = new FakeLogger<PgSweeper>();
        await WithSweeperAsync(failFirst: true, logger, async (sweeper, time, sweep) =>
        {
            sweeper.Start();
            time.Advance(Interval);
            await sweep.FirstCall.WaitAsync(WaitLimit, cancellationToken);
            time.Advance(Interval);
            await sweep.SecondCall.WaitAsync(WaitLimit, cancellationToken);

            var records = logger.Collector.GetSnapshot();

            var record = Assert.Single(records);
            Assert.Equal(EventIds.SweepFailed, record.Id.Id);
            Assert.Equal(LogLevel.Error, record.Level);
            Assert.IsType<InvalidOperationException>(record.Exception);
        });
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_Started_StopsTheLoop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweeperAsync(failFirst: false, async (sweeper, time, sweep) =>
        {
            sweeper.Start();
            time.Advance(Interval);
            await sweep.FirstCall.WaitAsync(WaitLimit, cancellationToken);

            await sweeper.DisposeAsync().AsTask().WaitAsync(WaitLimit, cancellationToken);

            time.Advance(Interval);

            Assert.Equal(1, sweep.Calls);
        });
    }

    #endregion
}
