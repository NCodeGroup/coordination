// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class CoordinationFixedWindowLimiterTests
{
    private const int RacingCallers = 64;

    private static TimeSpan Window { get; } = TimeSpan.FromMinutes(1);

    private static FixedWindowLimit Limit { get; } = new(2, Window);

    private static CoordinationKey CreateKey() => RecipeTesting.Keyspace.CreateKey("limiter");

    private static CoordinationFixedWindowLimiter CreateLimiter(
        ServiceProvider services,
        FakeTimeProvider time,
        FixedWindowLimit limit
    )
    {
        var counters = services.GetRequiredService<ICounterService>();
        var key = CreateKey();
        return new CoordinationFixedWindowLimiter(counters, time, key, limit);
    }

    #region AcquireAsync Tests

    [Fact]
    public async Task AcquireAsync_WhenTheLimitHasPermits_AcquiresThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);

        using var first = await limiter.AcquireAsync(1, cancellationToken);
        using var second = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenTheWindowIsFull_DeniesWithTheTimeToTheWindowEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var first = await limiter.AcquireAsync(1, cancellationToken);
        using var second = await limiter.AcquireAsync(1, cancellationToken);
        var elapsed = TimeSpan.FromSeconds(10);
        time.Advance(elapsed);

        using var third = await limiter.AcquireAsync(1, cancellationToken);

        var found = third.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        var expected = TimeSpan.FromSeconds(50);
        Assert.False(third.IsAcquired);
        Assert.True(found);
        Assert.Equal(expected, retryAfter);
    }

    [Fact]
    public async Task AcquireAsync_WhenTheWindowHasPassed_AcquiresAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var first = await limiter.AcquireAsync(2, cancellationToken);
        time.Advance(Window);

        using var next = await limiter.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.True(next.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenAnotherLimiterUsesTheKey_SharesTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var one = CreateLimiter(services, time, Limit);
        using var other = CreateLimiter(services, time, Limit);
        using var first = await one.AcquireAsync(2, cancellationToken);

        using var second = await other.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task AcquireAsync_WhenThePermitCountIsOutOfRange_Throws(int permitCount)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await limiter.AcquireAsync(permitCount, cancellationToken)
        );
    }

    [Fact]
    public async Task AcquireAsync_WhenDisposed_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        limiter.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await limiter.AcquireAsync(1, cancellationToken)
        );
    }

    [Fact]
    public async Task AcquireAsync_WhenCallersRace_AcquiresExactlyTheLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var limit = new FixedWindowLimit(10, Window);
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

    #region AttemptAcquire Tests

    [Fact]
    public async Task AttemptAcquire_WhenTheLimitHasPermits_IsNotAcquired()
    {
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);

        using var lease = limiter.AttemptAcquire(1);

        Assert.False(lease.IsAcquired);
    }

    #endregion

    #region IdleDuration Property Tests

    [Fact]
    public async Task IdleDuration_AfterAnAcquireAndAnAdvance_IsTheAdvance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        using var limiter = CreateLimiter(services, time, Limit);
        using var lease = await limiter.AcquireAsync(1, cancellationToken);

        var elapsed = TimeSpan.FromSeconds(5);
        time.Advance(elapsed);

        var idle = limiter.IdleDuration;
        Assert.Equal(elapsed, idle);
    }

    #endregion
}
