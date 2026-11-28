// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Sweep.Tests;

public sealed class PgSweeperTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private sealed class CountingTimeProvider : FakeTimeProvider
    {
        private readonly TaskCompletionSource _first = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _timers;

        public int Timers => Volatile.Read(ref _timers);

        public Task FirstTimer => _first.Task;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            Interlocked.Increment(ref _timers);
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _first.TrySetResult();
            return timer;
        }
    }

    private static async Task WithSweeperAsync(Func<PgSweeper, CountingTimeProvider, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var server = new PgServer(source, PgSettings.Default);
        var keyspaces = new PgKeyspaces(server, PgSettings.Default.Schema);
        var table = new PgRecordTable(server, keyspaces, PgSettings.Default);
        var sweeps = new PgSweeps(server, PgSettings.Default, table);
        var time = new CountingTimeProvider();

        await using var sweeper = new PgSweeper(sweeps, Interval, time);
        await test(sweeper, time);
    }

    #region Start Tests

    [Fact]
    public async Task Start_Twice_StartsOneLoop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweeperAsync(
            async (sweeper, time) =>
            {
                sweeper.Start();
                sweeper.Start();

                await time.FirstTimer.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                time.Advance(Interval);

                var elapsed = Stopwatch.StartNew();
                while (time.Timers < 2)
                {
                    Assert.True(
                        elapsed.Elapsed < TimeSpan.FromSeconds(60),
                        "The loop did not wait for its next pass."
                    );
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                }

                await sweeper.DisposeAsync();

                Assert.Equal(2, time.Timers);
            }
        );
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_Started_StopsTheLoop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSweeperAsync(
            async (sweeper, time) =>
            {
                sweeper.Start();
                await time.FirstTimer.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                await sweeper
                    .DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                var timers = time.Timers;
                time.Advance(Interval);

                Assert.Equal(timers, time.Timers);
            }
        );
    }

    #endregion
}
