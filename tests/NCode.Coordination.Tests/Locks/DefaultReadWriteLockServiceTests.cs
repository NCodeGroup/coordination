// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.InProcess.Tests;
using NCode.Coordination.Recipes.Tests;

namespace NCode.Coordination.Locks.Tests;

public sealed class DefaultReadWriteLockServiceTests
{
    private const int RacingCallers = 32;

    private static TimeSpan TermsLength { get; } = TimeSpan.FromSeconds(30);

    private static TimeToLive TermsTimeToLive { get; } = new(TermsLength);

    private static LeaseTerms Terms { get; } = LeaseTerms.ForTimeToLive(TermsTimeToLive);

    private static CoordinationKey CreateResource() => RecipeTesting.Keyspace.CreateKey("resource");

    private static DefaultReadWriteLockService CreateLocks(IServiceProvider services)
    {
        var leases = services.GetRequiredService<ILeaseService>();
        var records = services.GetRequiredService<IRecordService>();
        var watches = services.GetRequiredService<IRecordWatchService>();

        return new(leases, records, watches);
    }

    #region AcquireReadAsync Tests

    [Fact]
    public async Task AcquireReadAsync_WhenAnotherReaderHolds_SharesTheLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var locks = CreateLocks(services);
        var resource = CreateResource();
        var first = await locks.AcquireReadAsync(resource, Terms, null, cancellationToken);
        Assert.NotNull(first);

        var second = await locks.AcquireReadAsync(resource, Terms, null, cancellationToken);

        Assert.NotNull(second);
        Assert.NotEqual(first.Registration, second.Registration);
        Assert.True(second.FencingToken > first.FencingToken);
    }

    [Fact]
    public async Task AcquireReadAsync_WhenAWriterHolds_WaitsForItsRelease()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var locks = CreateLocks(services);
        var resource = CreateResource();
        var write = await locks.AcquireWriteAsync(resource, Terms, null, cancellationToken);
        Assert.NotNull(write);

        var task = locks.AcquireReadAsync(resource, Terms, null, cancellationToken).AsTask();

        Assert.False(task.IsCompleted);

        var released = await locks.ReleaseWriteAsync(write, null, cancellationToken);
        var read = await task;

        Assert.True(released);
        Assert.NotNull(read);
        Assert.True(read.FencingToken > write.FencingToken);
    }

    [Fact]
    public async Task AcquireReadAsync_WhenAfterAWrite_HasAGreaterFencingToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var locks = CreateLocks(services);
        var resource = CreateResource();
        var write = await locks.AcquireWriteAsync(resource, Terms, null, cancellationToken);
        Assert.NotNull(write);
        var released = await locks.ReleaseWriteAsync(write, null, cancellationToken);
        Assert.True(released);

        var read = await locks.AcquireReadAsync(resource, Terms, null, cancellationToken);

        Assert.NotNull(read);
        Assert.True(read.FencingToken > write.FencingToken);
    }

    [Fact]
    public async Task AcquireReadAsync_WhenTheSessionHasEnded_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var sessions = services.GetRequiredService<ISessionService>();
        var locks = CreateLocks(services);
        var resource = CreateResource();
        var session = await RecipeTesting.OpenSessionAsync(services, cancellationToken);
        await sessions.CloseAsync(session, null, cancellationToken);
        var terms = LeaseTerms.ForSession(session);

        var grant = await locks.AcquireReadAsync(resource, terms, null, cancellationToken);

        Assert.Null(grant);
    }

    #endregion

    #region ReleaseReadAsync Tests

    [Fact]
    public async Task ReleaseReadAsync_WhenReleasedTwice_ReturnsFalse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var locks = CreateLocks(services);
        var resource = CreateResource();
        var read = await locks.AcquireReadAsync(resource, Terms, null, cancellationToken);
        Assert.NotNull(read);
        var first = await locks.ReleaseReadAsync(read, null, cancellationToken);
        Assert.True(first);

        var second = await locks.ReleaseReadAsync(read, null, cancellationToken);

        Assert.False(second);
    }

    #endregion

    #region AcquireWriteAsync Tests

    [Fact]
    public async Task AcquireWriteAsync_WhenAReaderHolds_WaitsForItsRelease()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var locks = CreateLocks(services);
        var resource = CreateResource();
        var read = await locks.AcquireReadAsync(resource, Terms, null, cancellationToken);
        Assert.NotNull(read);

        var task = locks.AcquireWriteAsync(resource, Terms, null, cancellationToken).AsTask();

        Assert.False(task.IsCompleted);

        var released = await locks.ReleaseReadAsync(read, null, cancellationToken);
        var write = await task;

        Assert.True(released);
        Assert.NotNull(write);
        Assert.True(write.FencingToken > read.FencingToken);
    }

    [Fact]
    public async Task AcquireWriteAsync_WhenTheResourceHasTheMostSegments_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var locks = CreateLocks(services);
        var segments = new KeySegment[CoordinationKey.MaxSegments];
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = KeySegment.FromInt64(i);
        }

        var resource = new CoordinationKey(RecipeTesting.Keyspace, segments);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await locks.AcquireWriteAsync(resource, Terms, null, cancellationToken)
        );

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public async Task AcquireWriteAsync_WhenTheStreamEndsWithoutAFailure_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var leases = services.GetRequiredService<ILeaseService>();
        var records = services.GetRequiredService<IRecordService>();
        var locks = CreateLocks(services);
        var ending = new EndingWatches();
        var endingLocks = new DefaultReadWriteLockService(leases, records, ending);
        var resource = CreateResource();
        var read = await locks.AcquireReadAsync(resource, Terms, null, cancellationToken);
        Assert.NotNull(read);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            endingLocks.AcquireWriteAsync(resource, Terms, null, cancellationToken).AsTask()
        );
        var retry = locks.AcquireWriteAsync(resource, Terms, null, cancellationToken).AsTask();

        Assert.Equal("The watch ended without a failure.", exception.Message);
        Assert.False(retry.IsCompleted);

        var released = await locks.ReleaseReadAsync(read, null, cancellationToken);
        var write = await retry;

        Assert.True(released);
        Assert.NotNull(write);
    }

    [Fact]
    public async Task AcquireWriteAsync_WhenReadersAndWritersRace_HoldsEachWriterAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        await using var services = RecipeTesting.Build(time);
        var locks = CreateLocks(services);
        var resource = CreateResource();
        var counters = new int[2];

        await RecipeTesting.RunConcurrentlyAsync(
            RacingCallers,
            async index =>
            {
                if (index % 2 == 0)
                {
                    var write = await locks.AcquireWriteAsync(
                        resource,
                        Terms,
                        null,
                        cancellationToken
                    );
                    Assert.NotNull(write);

                    var writers = Interlocked.Increment(ref counters[0]);
                    var readers = Volatile.Read(ref counters[1]);
                    await Task.Yield();
                    Interlocked.Decrement(ref counters[0]);

                    var released = await locks.ReleaseWriteAsync(write, null, cancellationToken);
                    Assert.True(released);
                    Assert.Equal(1, writers);
                    Assert.Equal(0, readers);
                }
                else
                {
                    var read = await locks.AcquireReadAsync(
                        resource,
                        Terms,
                        null,
                        cancellationToken
                    );
                    Assert.NotNull(read);

                    Interlocked.Increment(ref counters[1]);
                    var writers = Volatile.Read(ref counters[0]);
                    await Task.Yield();
                    Interlocked.Decrement(ref counters[1]);

                    var released = await locks.ReleaseReadAsync(read, null, cancellationToken);
                    Assert.True(released);
                    Assert.Equal(0, writers);
                }

                return 0;
            },
            cancellationToken
        );

        var finalWriters = Volatile.Read(ref counters[0]);
        var finalReaders = Volatile.Read(ref counters[1]);

        Assert.Equal(0, finalWriters);
        Assert.Equal(0, finalReaders);
    }

    #endregion

    private sealed class EndingWatches : IRecordWatchService
    {
        public async IAsyncEnumerable<RecordChange> WatchKeyAsync(
            CoordinationKey key,
            Revision? after,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await Task.Yield();

            yield break;
        }

        public async IAsyncEnumerable<RecordChange> WatchPrefixAsync(
            CoordinationKey prefix,
            Revision? after,
            OperationOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await Task.Yield();

            yield break;
        }
    }
}
