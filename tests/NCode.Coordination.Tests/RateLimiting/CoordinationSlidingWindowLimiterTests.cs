// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class CoordinationSlidingWindowLimiterTests
{
    private const int RacingCallers = 64;

    private static TimeSpan Window { get; } = TimeSpan.FromSeconds(2);

    private static SlidingWindowLimit Limit { get; } = new(2, Window, 2);

    private static CoordinationKey CreateKey() => RecipeTesting.Keyspace.CreateKey("limiter");

    private static CoordinationSlidingWindowLimiter CreateLimiter(
        ServiceProvider services,
        FakeTimeProvider time,
        SlidingWindowLimit limit
    )
    {
        var counters = services.GetRequiredService<ICounterService>();
        var key = CreateKey();
        return new CoordinationSlidingWindowLimiter(counters, time, key, limit);
    }

    #region AcquireAsync Tests

    [Fact]
    public async Task AcquireAsync_WhenAFixedWindowWouldReset_StillCountsThePreviousSegment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        var firstElapsed = TimeSpan.FromMilliseconds(1900);
        time.Advance(firstElapsed);
        using var first = await limiter.AcquireAsync(2, cancellationToken);
        var secondElapsed = TimeSpan.FromMilliseconds(100);
        time.Advance(secondElapsed);

        using var second = await limiter.AcquireAsync(1, cancellationToken);

        var found = second.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        var expected = TimeSpan.FromSeconds(1);
        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(found);
        Assert.Equal(expected, retryAfter);
    }

    [Fact]
    public async Task AcquireAsync_WhenTheWindowIsTheLongestAllowed_AcquiresThenDenies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var length = TimeSpan.FromDays(180);
        var limit = new SlidingWindowLimit(1, length, 1);
        using var limiter = CreateLimiter(services, time, limit);

        using var first = await limiter.AcquireAsync(1, cancellationToken);
        using var second = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenASegmentSlidesOut_FreesItsPermits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var first = await limiter.AcquireAsync(2, cancellationToken);
        var elapsed = TimeSpan.FromSeconds(1);
        time.Advance(elapsed);
        using var second = await limiter.AcquireAsync(1, cancellationToken);
        time.Advance(elapsed);

        using var third = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(third.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenCallersRace_AcquiresExactlyTheLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var window = TimeSpan.FromMinutes(1);
        var limit = new SlidingWindowLimit(10, window, 4);
        using var limiter = CreateLimiter(services, time, limit);

        var results = await RecipeTesting.RunConcurrentlyAsync<bool>(
            RacingCallers,
            async _ =>
            {
                using var lease = await limiter.AcquireAsync(1, cancellationToken);
                return lease.IsAcquired;
            },
            cancellationToken
        );

        var acquired = results.Count(result => result);
        Assert.Equal(10, acquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenTheWindowHasSeveralSegments_ReadsTheEarlierSegmentsTogether()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var length = TimeSpan.FromSeconds(4);
        var limit = new SlidingWindowLimit(4, length, 4);
        var counters = services.GetRequiredService<ICounterService>();
        var gated = new GatedCounters(counters, 3);
        var key = CreateKey();
        using var limiter = new CoordinationSlidingWindowLimiter(gated, time, key, limit);

        using var lease = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(lease.IsAcquired);
    }

    #endregion

    private sealed class GatedCounters(ICounterService inner, int expectedReads) : ICounterService
    {
        private int _started;

        private static TimeSpan ReadTimeout { get; } = TimeSpan.FromSeconds(15);

        private TaskCompletionSource Gate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<long> IncrementAsync(
            CoordinationKey key,
            long amount,
            TimeToLive timeToLive,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => inner.IncrementAsync(key, amount, timeToLive, options, cancellationToken);

        public async ValueTask<long> ReadAsync(
            CoordinationKey key,
            OperationOptions? options,
            CancellationToken cancellationToken
        )
        {
            var started = Interlocked.Increment(ref _started);
            if (started == expectedReads)
            {
                Gate.TrySetResult();
            }

            await Gate.Task.WaitAsync(ReadTimeout, cancellationToken);

            return await inner.ReadAsync(key, options, cancellationToken);
        }

        public ValueTask ResetAsync(
            CoordinationKey key,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => inner.ResetAsync(key, options, cancellationToken);

        public ValueTask<CounterBatchResult> IncrementBatchAsync(
            IReadOnlyList<CounterIncrement> increments,
            OperationOptions? options,
            CancellationToken cancellationToken
        ) => inner.IncrementBatchAsync(increments, options, cancellationToken);
    }
}
