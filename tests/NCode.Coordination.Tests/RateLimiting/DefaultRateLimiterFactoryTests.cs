// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.RateLimiting.Tests;

public sealed class DefaultRateLimiterFactoryTests
{
    private static DefaultRateLimiterFactory CreateFactory(
        ServiceProvider services,
        FakeTimeProvider time
    )
    {
        var counters = services.GetRequiredService<ICounterService>();
        var records = services.GetRequiredService<IRecordService>();
        return new DefaultRateLimiterFactory(counters, records, time);
    }

    private static PartitionedRateLimiter<string> CreatePartitionedLimiter(
        DefaultRateLimiterFactory factory
    )
    {
        var window = TimeSpan.FromMinutes(1);
        var limit = new FixedWindowLimit(2, window);
        return PartitionedRateLimiter.Create<string, string>(id => RateLimitPartition.Get(id, x =>
            {
                var key = RecipeTesting.Keyspace.CreateKey("api", x);
                return factory.CreateFixedWindowLimiter(key, limit);
            }));
    }

    #region CreateFixedWindowLimiter Tests

    [Fact]
    public async Task CreateFixedWindowLimiter_WhenTwoPartitionedLimitersShareAKey_ShareTheCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var factory = CreateFactory(services, time);
        using var one = CreatePartitionedLimiter(factory);
        using var other = CreatePartitionedLimiter(factory);

        using var first = await one.AcquireAsync("alice", 1, cancellationToken);
        using var second = await other.AcquireAsync("alice", 1, cancellationToken);
        using var third = await one.AcquireAsync("alice", 1, cancellationToken);
        using var bob = await one.AcquireAsync("bob", 1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
        Assert.True(bob.IsAcquired);
    }

    #endregion

    #region CreateSlidingWindowLimiter Tests

    [Fact]
    public async Task CreateSlidingWindowLimiter_WhenTheKeyHasTheMostSegments_Throws()
    {
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var factory = CreateFactory(services, time);
        var segments = new KeySegment[CoordinationKey.MaxSegments];
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = $"segment-{i}";
        }

        var key = new CoordinationKey(RecipeTesting.Keyspace, segments);
        var window = TimeSpan.FromSeconds(2);
        var limit = new SlidingWindowLimit(2, window, 2);

        var exception = Assert.Throws<ArgumentException>(() =>
            factory.CreateSlidingWindowLimiter(key, limit)
        );

        Assert.Equal("key", exception.ParamName);
    }

    #endregion

    #region CreateTokenBucketLimiter Tests

    [Fact]
    public async Task CreateTokenBucketLimiter_WhenTwoLimitersShareAKey_ShareTheBucket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var factory = CreateFactory(services, time);
        var key = RecipeTesting.Keyspace.CreateKey("bucket");
        var period = TimeSpan.FromSeconds(1);
        var limit = new TokenBucketLimit(2, 1, period);
        using var one = factory.CreateTokenBucketLimiter(key, limit);
        using var other = factory.CreateTokenBucketLimiter(key, limit);

        using var first = await one.AcquireAsync(1, cancellationToken);
        using var second = await other.AcquireAsync(1, cancellationToken);
        using var third = await one.AcquireAsync(1, cancellationToken);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
    }

    #endregion
}
