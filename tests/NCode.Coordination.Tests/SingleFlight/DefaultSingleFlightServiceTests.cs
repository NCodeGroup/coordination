// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.SingleFlight.Tests;

public sealed class DefaultSingleFlightServiceTests
{
    private const int RacingCallers = 16;

    private static SingleFlightTerms Terms { get; } =
        new(new TimeToLive(TimeSpan.FromMinutes(1)), new TimeToLive(TimeSpan.FromSeconds(30)));

    private static ReadOnlyMemory<byte> Value { get; } = Encoding.UTF8.GetBytes("fetched");

    private static CoordinationKey CreateKey() => RecipeTesting.Keyspace.CreateKey("flight");

    private static DefaultSingleFlightService CreateService(IServiceProvider services) =>
        new(
            services.GetRequiredService<IRecordService>(),
            services.GetRequiredService<ILeaseService>()
        );

    #region GetOrFetchAsync Tests

    [Fact]
    public async Task GetOrFetchAsync_WhenTheValueIsStored_ReturnsItWithoutFetching()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var records = services.GetRequiredService<IRecordService>();
        var flights = CreateService(services);
        var key = CreateKey();
        var created = await records.CreateAsync(
            key,
            Value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        var fetches = 0;

        var stored = await flights.GetOrFetchAsync(
            key,
            Terms,
            _ =>
            {
                Interlocked.Increment(ref fetches);
                return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
            },
            null,
            cancellationToken
        );

        Assert.Equal(0, fetches);
        Assert.Equal(Value.ToArray(), stored.Value.ToArray());
        Assert.Equal(created.Revision, stored.Revision);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenAbsent_FetchesAndStoresTheValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var records = services.GetRequiredService<IRecordService>();
        var flights = CreateService(services);
        var key = CreateKey();

        var stored = await flights.GetOrFetchAsync(
            key,
            Terms,
            _ => ValueTask.FromResult(Value),
            null,
            cancellationToken
        );

        var read = await records.ReadAsync(key, null, cancellationToken);
        Assert.NotNull(read);
        Assert.Equal(Value.ToArray(), stored.Value.ToArray());
        Assert.Equal(Value.ToArray(), read.Value.ToArray());
        Assert.Equal(read.Revision, stored.Revision);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenCallersRace_FetchesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var flights = CreateService(services);
        var key = CreateKey();
        var fetches = 0;
        var fetchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var callers = RecipeTesting.RunConcurrentlyAsync(
            RacingCallers,
            async _ =>
            {
                var stored = await flights.GetOrFetchAsync(
                    key,
                    Terms,
                    async token =>
                    {
                        Interlocked.Increment(ref fetches);
                        fetchStarted.TrySetResult();
                        await release.Task.WaitAsync(token);
                        return Value;
                    },
                    null,
                    cancellationToken
                );

                return Convert.ToHexString(stored.Value.Span);
            },
            cancellationToken
        );
        await fetchStarted.Task.WaitAsync(cancellationToken);
        release.SetResult();
        var values = await callers;

        Assert.Equal(1, fetches);
        Assert.Equal(RacingCallers, values.Length);
        Assert.All(values, value => Assert.Equal(Convert.ToHexString(Value.Span), value));
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheFetchFails_LetsTheNextCallerFetch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var flights = CreateService(services);
        var key = CreateKey();
        var fetches = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await flights.GetOrFetchAsync(
                key,
                Terms,
                _ =>
                {
                    Interlocked.Increment(ref fetches);
                    throw new InvalidOperationException("The fetch failed.");
                },
                null,
                cancellationToken
            )
        );
        var stored = await flights.GetOrFetchAsync(
            key,
            Terms,
            _ =>
            {
                Interlocked.Increment(ref fetches);
                return ValueTask.FromResult(Value);
            },
            null,
            cancellationToken
        );

        Assert.Equal(2, fetches);
        Assert.Equal(Value.ToArray(), stored.Value.ToArray());
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheValueExpires_FetchesAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var flights = CreateService(services);
        var key = CreateKey();
        var fetches = 0;
        ValueTask<ReadOnlyMemory<byte>> Fetch(CancellationToken token)
        {
            Interlocked.Increment(ref fetches);
            return ValueTask.FromResult(Value);
        }

        await flights.GetOrFetchAsync(key, Terms, Fetch, null, cancellationToken);
        time.Advance(TimeSpan.FromMinutes(2));
        await flights.GetOrFetchAsync(key, Terms, Fetch, null, cancellationToken);

        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheKeyHasTheMostSegments_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = RecipeTesting.Build(InProcessTesting.CreateTime());
        var flights = CreateService(services);
        var segments = new KeySegment[CoordinationKey.MaxSegments];
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = KeySegment.FromInt64(i);
        }

        var key = new CoordinationKey(RecipeTesting.Keyspace, segments);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await flights.GetOrFetchAsync(
                key,
                Terms,
                _ => ValueTask.FromResult(Value),
                null,
                cancellationToken
            )
        );

        Assert.Equal("key", exception.ParamName);
    }

    #endregion
}
