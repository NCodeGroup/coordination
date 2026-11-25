// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessConcurrencyTests
{
    private const int RacingCallers = 64;
    private const int CountingCallers = 1_000;

    private static Keyspace Keyspace { get; } =
        new("concurrency", KeySegment.FromGuid(Guid.NewGuid()));

    private static InProcessProvider CreateProvider() => new(TimeProvider.System);

    private static TService GetService<TService>(InProcessProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService)) as TService;
        Assert.NotNull(service);
        return service;
    }

    private static async Task<TResult[]> RunCallersAsync<TResult>(
        int count,
        Func<int, ValueTask<TResult>> caller
    )
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tasks = new Task<TResult>[count];
        for (var index = 0; index < count; index++)
        {
            var callerIndex = index;
            tasks[index] = Task.Run(async () => await caller(callerIndex), cancellationToken);
        }

        return await Task.WhenAll(tasks);
    }

    private static void AssertEachNumberOnce(long[] numbers, int count)
    {
        Assert.Equal(Enumerable.Range(1, count).Select(x => (long)x), numbers.Order());
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WhenCallersRace_WritesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var records = GetService<IRecordService>(CreateProvider());
        var key = Keyspace.CreateKey("record");

        var results = await RunCallersAsync(RacingCallers, index =>
            records.CreateAsync(
                key,
                BitConverter.GetBytes(index),
                RecordLifetime.Persistent,
                null,
                cancellationToken
            )
        );

        Assert.Single(results, x => x.IsWritten);
        Assert.Equal(RacingCallers - 1, results.Count(x => x.Status == RecordWriteStatus.Conflict));
    }

    #endregion

    #region TryRecordAsync Tests

    [Fact]
    public async Task TryRecordAsync_WhenCallersRace_RecordsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var occurrences = GetService<IOccurrenceService>(CreateProvider());
        var key = Keyspace.CreateKey("occurrence");

        var results = await RunCallersAsync(RacingCallers, _ =>
            occurrences.TryRecordAsync(
                key,
                new TimeToLive(TimeSpan.FromSeconds(30)),
                null,
                cancellationToken
            )
        );

        Assert.Single(results, x => x == OccurrenceStatus.Recorded);
        Assert.Equal(RacingCallers - 1, results.Count(x => x == OccurrenceStatus.Duplicate));
    }

    #endregion

    #region IncrementAsync Tests

    [Fact]
    public async Task IncrementAsync_WhenCallersRace_CountsEachIncrement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var counters = GetService<ICounterService>(CreateProvider());
        var key = Keyspace.CreateKey("counter");
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));

        var counts = await RunCallersAsync(CountingCallers, _ =>
            counters.IncrementAsync(key, 1, timeToLive, null, cancellationToken)
        );

        AssertEachNumberOnce(counts, CountingCallers);
        Assert.Equal(CountingCallers, await counters.ReadAsync(key, null, cancellationToken));
    }

    #endregion

    #region NextAsync Tests

    [Fact]
    public async Task NextAsync_WhenCallersRace_HandsOutEachNumberOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sequences = GetService<ISequenceService>(CreateProvider());
        var key = Keyspace.CreateKey("sequence");

        var numbers = await RunCallersAsync(CountingCallers, _ =>
            sequences.NextAsync(key, null, cancellationToken)
        );

        AssertEachNumberOnce(numbers, CountingCallers);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenCallersRace_AppliesOneSuccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var transactions = GetService<ITransactionService>(CreateProvider());
        var key = Keyspace.CreateKey("transaction");

        var results = await RunCallersAsync(RacingCallers, index =>
            transactions.ExecuteAsync(
                new RecordTransaction(
                    [RecordCondition.Absent(key)],
                    [RecordWrite.Put(key, BitConverter.GetBytes(index), RecordLifetime.Persistent)]
                ),
                null,
                cancellationToken
            )
        );

        Assert.Single(results, x => x.Succeeded);
        Assert.Equal(RacingCallers - 1, results.Count(x => !x.Succeeded));
    }

    #endregion
}
