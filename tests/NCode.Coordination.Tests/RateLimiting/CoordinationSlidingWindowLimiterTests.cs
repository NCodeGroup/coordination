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

    private static SlidingWindowLimit Limit { get; } = new(2, TimeSpan.FromSeconds(2), 2);

    private static CoordinationKey CreateKey() => RecipeTesting.Keyspace.CreateKey("limiter");

    private static CoordinationSlidingWindowLimiter CreateLimiter(
        ServiceProvider services,
        FakeTimeProvider time,
        SlidingWindowLimit limit
    )
    {
        var counters = services.GetRequiredService<ICounterService>();
        return new CoordinationSlidingWindowLimiter(counters, time, CreateKey(), limit);
    }

    #region AcquireAsync Tests

    [Fact]
    public async Task AcquireAsync_WhenAFixedWindowWouldReset_StillCountsThePreviousSegment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        time.Advance(TimeSpan.FromMilliseconds(1900));
        using var first = await limiter.AcquireAsync(2, cancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(100));

        using var second = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(second.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);
    }

    [Fact]
    public async Task AcquireAsync_WhenASegmentSlidesOut_FreesItsPermits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var first = await limiter.AcquireAsync(2, cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        using var second = await limiter.AcquireAsync(1, cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));

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
        using var limiter = CreateLimiter(
            services,
            time,
            new SlidingWindowLimit(10, TimeSpan.FromMinutes(1), 4)
        );

        var results = await RecipeTesting.RunConcurrentlyAsync<bool>(
            RacingCallers,
            async _ =>
            {
                using var lease = await limiter.AcquireAsync(1, cancellationToken);
                return lease.IsAcquired;
            },
            cancellationToken
        );

        Assert.Equal(10, results.Count(acquired => acquired));
    }

    #endregion
}
