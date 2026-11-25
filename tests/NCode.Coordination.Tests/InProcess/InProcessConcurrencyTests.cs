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
        Func<int, ValueTask<TResult>> caller,
        CancellationToken cancellationToken
    )
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new Task<TResult>[count];
        for (var index = 0; index < count; index++)
        {
            var callerIndex = index;
            tasks[index] = Task.Run(
                async () =>
                {
                    await gate.Task.WaitAsync(cancellationToken);
                    return await caller(callerIndex);
                },
                cancellationToken
            );
        }

        gate.SetResult();

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

        var results = await RunCallersAsync(
            RacingCallers,
            index =>
            {
                var value = BitConverter.GetBytes(index);
                return records.CreateAsync(
                    key,
                    value,
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                );
            },
            cancellationToken
        );

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

        var results = await RunCallersAsync(
            RacingCallers,
            _ => occurrences.TryRecordAsync(key, timeToLive, null, cancellationToken),
            cancellationToken
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

        var counts = await RunCallersAsync(
            CountingCallers,
            _ => counters.IncrementAsync(key, 1, timeToLive, null, cancellationToken),
            cancellationToken
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

        var numbers = await RunCallersAsync(
            CountingCallers,
            _ => sequences.NextAsync(key, null, cancellationToken),
            cancellationToken
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

        var results = await RunCallersAsync(
            RacingCallers,
            index =>
            {
                var value = BitConverter.GetBytes(index);
                var put = RecordWrite.Put(key, value, RecordLifetime.Persistent);
                var transaction = new RecordTransaction([absent], [put]);
                return transactions.ExecuteAsync(transaction, null, cancellationToken);
            },
            cancellationToken
        );

        var failures = results.Count(x => !x.Succeeded);

        Assert.Single(results, x => x.Succeeded);
        Assert.Equal(RacingCallers - 1, failures);
    }

    #endregion

    #region TryAcquireAsync Tests

    [Fact]
    public async Task TryAcquireAsync_WhenCallersRace_GrantsOneLease()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = CreateProvider();
        var leases = GetService<ILeaseService>(provider);
        var resource = Keyspace.CreateKey("lease");
        var ttl = TimeSpan.FromSeconds(30);
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(ttl));

        var results = await RunCallersAsync(
            RacingCallers,
            _ => leases.TryAcquireAsync(resource, terms, null, cancellationToken),
            cancellationToken
        );

        var held = results.Count(x => x.Status == LeaseAcquireStatus.Held);

        Assert.Single(results, x => x.IsAcquired);
        Assert.Equal(RacingCallers - 1, held);
    }

    [Fact]
    public async Task TryAcquireAsync_WhenCallersRaceForASemaphore_FillsEachSlotOnce()
    {
        const int capacity = 4;
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = CreateProvider();
        var semaphores = GetService<ISemaphoreService>(provider);
        var resource = Keyspace.CreateKey("semaphore");
        var ttl = TimeSpan.FromSeconds(30);
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(ttl));

        var results = await RunCallersAsync(
            RacingCallers,
            _ => semaphores.TryAcquireAsync(resource, capacity, terms, null, cancellationToken),
            cancellationToken
        );

        var slots = results
            .Where(x => x.IsAcquired)
            .Select(x => x.Grant is { } grant ? grant.Slot : -1);
        var full = results.Count(x => x.Status == SemaphoreAcquireStatus.Full);

        Assert.Equal([0, 1, 2, 3], slots.Order());
        Assert.Equal(RacingCallers - capacity, full);
    }

    #endregion

    #region TryLeadAsync Tests

    [Fact]
    public async Task TryLeadAsync_WhenCallersRace_ElectsOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = CreateProvider();
        var elections = GetService<IElectionService>(provider);
        var role = Keyspace.CreateKey("role");
        var ttl = TimeSpan.FromSeconds(30);
        var terms = LeaseTerms.ForTimeToLive(new TimeToLive(ttl));

        var results = await RunCallersAsync(
            RacingCallers,
            index =>
            {
                var value = BitConverter.GetBytes(index);
                return elections.TryLeadAsync(role, value, terms, null, cancellationToken);
            },
            cancellationToken
        );

        var led = results.Count(x => x.Status == ElectionStatus.Led);

        Assert.Single(results, x => x.IsElected);
        Assert.Equal(RacingCallers - 1, led);
    }

    #endregion

    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_WhenCallersRace_ClaimsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = CreateProvider();
        var claims = GetService<IClaimService>(provider);
        var name = Keyspace.CreateKey("claim");
        var window = new TimeWindow(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(1));
        var ttl = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(ttl);

        var results = await RunCallersAsync(
            RacingCallers,
            _ => claims.TryClaimAsync(name, window, timeToLive, null, cancellationToken),
            cancellationToken
        );

        var taken = results.Count(x => x == ClaimStatus.Taken);

        Assert.Single(results, x => x == ClaimStatus.Claimed);
        Assert.Equal(RacingCallers - 1, taken);
    }

    #endregion

    #region JoinAsync Tests

    [Fact]
    public async Task JoinAsync_WhenSessionsRaceForANodeId_JoinsOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = CreateProvider();
        var sessions = GetService<ISessionService>(provider);
        var memberships = GetService<IMembershipService>(provider);
        var group = Keyspace.CreateKey("group");
        var ttl = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(ttl);
        var members = new Member[RacingCallers];
        for (var index = 0; index < RacingCallers; index++)
        {
            var grant = await sessions.OpenAsync(Keyspace, timeToLive, null, cancellationToken);
            members[index] = new Member(grant.Id, "node", "host:1");
        }

        var results = await RunCallersAsync(
            RacingCallers,
            index => memberships.JoinAsync(group, members[index], null, cancellationToken),
            cancellationToken
        );

        var taken = results.Count(x => x == MembershipJoinStatus.NodeIdTaken);

        Assert.Single(results, x => x == MembershipJoinStatus.Joined);
        Assert.Equal(RacingCallers - 1, taken);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenCallersRace_NumbersEachMessageOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = CreateProvider();
        var notifications = GetService<INotificationService>(provider);
        var topic = Keyspace.CreateKey("topic");
        await using var stream = notifications
            .SubscribeAsync(topic, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        Assert.True(await stream.MoveNextAsync());
        Assert.True(stream.Current.IsResync);

        await RunCallersAsync(
            CountingCallers,
            async index =>
            {
                var payload = BitConverter.GetBytes(index);
                await notifications.PublishAsync(topic, payload, null, cancellationToken);
                return true;
            },
            cancellationToken
        );

        var sequences = new List<long>();
        for (var index = 0; index < CountingCallers; index++)
        {
            Assert.True(await stream.MoveNextAsync());
            Assert.False(stream.Current.IsResync);
            sequences.Add(stream.Current.Sequence);
        }

        var expected = Enumerable.Range(1, CountingCallers).Select(x => (long)x);

        Assert.Equal(expected, sequences);
    }

    #endregion
}
