// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class CoordinationTokenBucketLimiterTests
{
    private const int RacingCallers = 32;

    private static TimeSpan ReplenishmentPeriod { get; } = TimeSpan.FromSeconds(1);

    private static TokenBucketLimit Limit { get; } = new(2, 1, ReplenishmentPeriod);

    private static CoordinationKey CreateKey() => RecipeTesting.Keyspace.CreateKey("limiter");

    private static CoordinationTokenBucketLimiter CreateLimiter(
        ServiceProvider services,
        FakeTimeProvider time,
        TokenBucketLimit limit
    )
    {
        var records = services.GetRequiredService<IRecordService>();
        var key = CreateKey();
        return new CoordinationTokenBucketLimiter(records, time, key, limit);
    }

    #region AcquireAsync Tests

    [Fact]
    public async Task AcquireAsync_WhenTheBucketIsEmpty_DeniesWithTheTimeToTheNextToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var first = await limiter.AcquireAsync(2, cancellationToken);

        using var second = await limiter.AcquireAsync(1, cancellationToken);

        var found = second.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(found);
        Assert.Equal(ReplenishmentPeriod, retryAfter);
    }

    [Fact]
    public async Task AcquireAsync_WhenThePeriodIsTheLongestAllowed_AcquiresThenDenies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var length = TimeSpan.FromDays(180);
        var limit = new TokenBucketLimit(1, 1, length);
        using var limiter = CreateLimiter(services, time, limit);

        using var first = await limiter.AcquireAsync(1, cancellationToken);
        using var second = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenAPeriodPasses_ReturnsOneToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var drained = await limiter.AcquireAsync(2, cancellationToken);
        time.Advance(ReplenishmentPeriod);

        using var first = await limiter.AcquireAsync(1, cancellationToken);
        using var second = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenManyPeriodsPass_CapsTheBucketAtTheLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var drained = await limiter.AcquireAsync(2, cancellationToken);
        var elapsed = TimeSpan.FromSeconds(10);
        time.Advance(elapsed);

        using var first = await limiter.AcquireAsync(1, cancellationToken);
        using var second = await limiter.AcquireAsync(1, cancellationToken);
        using var third = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenTwoLimitersShareAKey_SharesTheBucket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var one = CreateLimiter(services, time, Limit);
        using var other = CreateLimiter(services, time, Limit);

        using var first = await one.AcquireAsync(1, cancellationToken);
        using var second = await other.AcquireAsync(1, cancellationToken);
        using var third = await one.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenCallersRace_AcquiresExactlyTheLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var period = TimeSpan.FromMinutes(1);
        var limit = new TokenBucketLimit(10, 1, period);
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
