// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessConcurrencyTests
{
    private const int RacingCallers = 64;
    private const int CountingCallers = 1_000;

    private static Keyspace Keyspace { get; } = CreateKeyspace();

    private static Keyspace CreateKeyspace()
    {
        var id = Guid.NewGuid();
        var segment = KeySegment.FromGuid(id);
        return new Keyspace("concurrency", segment);
    }

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
        var expected = Enumerable.Range(1, count).Select(x => (long)x);
        var actual = numbers.Order();

        Assert.Equal(expected, actual);
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WhenCallersRace_WritesOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = CreateProvider();
        var records = GetService<IRecordService>(provider);
        var key = Keyspace.CreateKey("record");

        var results = await RunCallersAsync(RacingCallers, index =>
        {
            var value = BitConverter.GetBytes(index);
            return records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );
        });

        var conflicts = results.Count(x => x.Status == RecordWriteStatus.Conflict);

        Assert.Single(results, x => x.IsWritten);
        Assert.Equal(RacingCallers - 1, conflicts);
    }

    #endregion

    #region TryRecordAsync Tests

    [Fact]
    public async Task TryRecordAsync_WhenCallersRace_RecordsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = CreateProvider();
        var occurrences = GetService<IOccurrenceService>(provider);
        var key = Keyspace.CreateKey("occurrence");
        var ttl = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(ttl);

        var results = await RunCallersAsync(RacingCallers, _ =>
            occurrences.TryRecordAsync(key, timeToLive, null, cancellationToken)
        );

        var duplicates = results.Count(x => x == OccurrenceStatus.Duplicate);

        Assert.Single(results, x => x == OccurrenceStatus.Recorded);
        Assert.Equal(RacingCallers - 1, duplicates);
    }

    #endregion

    #region IncrementAsync Tests

    [Fact]
    public async Task IncrementAsync_WhenCallersRace_CountsEachIncrement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = CreateProvider();
        var counters = GetService<ICounterService>(provider);
        var key = Keyspace.CreateKey("counter");
        var ttl = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(ttl);

        var counts = await RunCallersAsync(CountingCallers, _ =>
            counters.IncrementAsync(key, 1, timeToLive, null, cancellationToken)
        );

        var total = await counters.ReadAsync(key, null, cancellationToken);

        AssertEachNumberOnce(counts, CountingCallers);
        Assert.Equal(CountingCallers, total);
    }

    #endregion

    #region NextAsync Tests

    [Fact]
    public async Task NextAsync_WhenCallersRace_HandsOutEachNumberOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = CreateProvider();
        var sequences = GetService<ISequenceService>(provider);
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
        var provider = CreateProvider();
        var transactions = GetService<ITransactionService>(provider);
        var key = Keyspace.CreateKey("transaction");
        var absent = RecordCondition.Absent(key);

        var results = await RunCallersAsync(RacingCallers, index =>
        {
            var value = BitConverter.GetBytes(index);
            var put = RecordWrite.Put(key, value, RecordLifetime.Persistent);
            var transaction = new RecordTransaction([absent], [put]);
            return transactions.ExecuteAsync(transaction, null, cancellationToken);
        });

        var failures = results.Count(x => !x.Succeeded);

        Assert.Single(results, x => x.Succeeded);
        Assert.Equal(RacingCallers - 1, failures);
    }

    #endregion
}
