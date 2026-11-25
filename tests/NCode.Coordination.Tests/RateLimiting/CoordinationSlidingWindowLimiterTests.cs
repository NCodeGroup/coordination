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

    #endregion
}
