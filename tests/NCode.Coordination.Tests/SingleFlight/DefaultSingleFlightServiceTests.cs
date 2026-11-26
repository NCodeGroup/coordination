// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.SingleFlight.Tests;

public sealed class DefaultSingleFlightServiceTests
{
    private const int RacingCallers = 16;

    private static TimeSpan ValueLength { get; } = TimeSpan.FromMinutes(1);

    private static TimeToLive ValueTimeToLive { get; } = new(ValueLength);

    private static TimeSpan FetchLength { get; } = TimeSpan.FromSeconds(30);

    private static TimeToLive FetchTimeToLive { get; } = new(FetchLength);

    private static SingleFlightTerms Terms { get; } = new(ValueTimeToLive, FetchTimeToLive);

    private static ReadOnlyMemory<byte> Value { get; } = "fetched"u8.ToArray();

    private static CoordinationKey CreateKey() => RecipeTesting.Keyspace.CreateKey("flight");

    private static DefaultSingleFlightService CreateService(IServiceProvider services)
    {
        var records = services.GetRequiredService<IRecordService>();
        var leases = services.GetRequiredService<ILeaseService>();

        return new(records, leases);
    }

    #region GetOrFetchAsync Tests

    [Fact]
    public async Task GetOrFetchAsync_WhenTheValueIsStored_ReturnsItWithoutFetching()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
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
        var expected = Value.ToArray();
        var storedBytes = stored.Value.ToArray();

        Assert.Equal(0, fetches);
        Assert.Equal(expected, storedBytes);
        Assert.Equal(created.Revision, stored.Revision);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenAbsent_FetchesAndStoresTheValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var records = services.GetRequiredService<IRecordService>();
        var flights = CreateService(services);
        var key = CreateKey();
        var fetched = ValueTask.FromResult(Value);

        var stored = await flights.GetOrFetchAsync(
            key,
            Terms,
            _ => fetched,
            null,
            cancellationToken
        );

        var read = await records.ReadAsync(key, null, cancellationToken);
        Assert.NotNull(read);
        var expected = Value.ToArray();
        var storedBytes = stored.Value.ToArray();
        var readBytes = read.Value.ToArray();
        Assert.Equal(expected, storedBytes);
        Assert.Equal(expected, readBytes);
        Assert.Equal(read.Revision, stored.Revision);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenCallersRace_FetchesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
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
        var expectedHex = Convert.ToHexString(Value.Span);

        Assert.Equal(1, fetches);
        Assert.Equal(RacingCallers, values.Length);
        Assert.All(values, value => Assert.Equal(expectedHex, value));
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheFetchingCallerIsCancelled_LetsAWaitingCallerFetch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var flights = CreateService(services);
        var key = CreateKey();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fetchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var never = new TaskCompletionSource<ReadOnlyMemory<byte>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var fetched = ValueTask.FromResult(Value);

        var first = flights
            .GetOrFetchAsync(
                key,
                Terms,
                async token =>
                {
                    fetchStarted.TrySetResult();
                    return await never.Task.WaitAsync(token);
                },
                null,
                stop.Token
            )
            .AsTask();
        await fetchStarted.Task.WaitAsync(cancellationToken);
        var second = flights
            .GetOrFetchAsync(key, Terms, _ => fetched, null, cancellationToken)
            .AsTask();

        Assert.False(second.IsCompleted);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var stored = await second.WaitAsync(cancellationToken);
        var expected = Value.ToArray();
        var storedBytes = stored.Value.ToArray();

        Assert.Equal(expected, storedBytes);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheFetchFails_LetsTheNextCallerFetch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
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
                    var failure = new InvalidOperationException("The fetch failed.");
                    return ValueTask.FromException<ReadOnlyMemory<byte>>(failure);
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
        var expected = Value.ToArray();
        var storedBytes = stored.Value.ToArray();

        Assert.Equal(2, fetches);
        Assert.Equal(expected, storedBytes);
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
        var elapsed = TimeSpan.FromMinutes(2);
        ValueTask<ReadOnlyMemory<byte>> Fetch(CancellationToken token)
        {
            Interlocked.Increment(ref fetches);
            return ValueTask.FromResult(Value);
        }

        await flights.GetOrFetchAsync(key, Terms, Fetch, null, cancellationToken);
        time.Advance(elapsed);
        await flights.GetOrFetchAsync(key, Terms, Fetch, null, cancellationToken);

        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheKeyHasTheMostSegments_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var flights = CreateService(services);
        var segments = new KeySegment[CoordinationKey.MaxSegments];
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = KeySegment.FromInt64(i);
        }

        var key = new CoordinationKey(RecipeTesting.Keyspace, segments);
        var fetched = ValueTask.FromResult(Value);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await flights.GetOrFetchAsync(key, Terms, _ => fetched, null, cancellationToken)
        );

        Assert.Equal("key", exception.ParamName);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheReleaseFailsAfterTheValueIsStored_ReturnsTheValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var records = services.GetRequiredService<IRecordService>();
        var leases = services.GetRequiredService<ILeaseService>();
        var failingLeases = new FailingReleaseLeases(leases);
        var flights = new DefaultSingleFlightService(records, failingLeases);
        var key = CreateKey();
        var fetched = ValueTask.FromResult(Value);

        var stored = await flights.GetOrFetchAsync(
            key,
            Terms,
            _ => fetched,
            null,
            cancellationToken
        );
        var expected = Value.ToArray();
        var storedBytes = stored.Value.ToArray();

        Assert.Equal(expected, storedBytes);
    }

    [Fact]
    public async Task GetOrFetchAsync_WhenTheFetchAndTheReleaseFail_ThrowsTheFetchFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var records = services.GetRequiredService<IRecordService>();
        var leases = services.GetRequiredService<ILeaseService>();
        var failingLeases = new FailingReleaseLeases(leases);
        var flights = new DefaultSingleFlightService(records, failingLeases);
        var key = CreateKey();
        var failure = new InvalidOperationException("The fetch failed.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await flights.GetOrFetchAsync(
                key,
                Terms,
                _ => ValueTask.FromException<ReadOnlyMemory<byte>>(failure),
                null,
                cancellationToken
            )
        );

        Assert.Equal("The fetch failed.", exception.Message);
    }

    #endregion
}
