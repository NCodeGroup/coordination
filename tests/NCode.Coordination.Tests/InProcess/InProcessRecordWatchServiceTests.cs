// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessRecordWatchServiceTests
{
    private static TimeSpan ShortTimeToLive { get; } = TimeSpan.FromMinutes(1);

    private static TService GetService<TService>(InProcessProvider provider)
        where TService : class
    {
        var service = provider.GetPrimitiveService(typeof(TService));
        return Assert.IsAssignableFrom<TService>(service);
    }

    private static async Task<Revision> CreateAsync(
        IRecordService records,
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        var value = "a"u8.ToArray();

        var result = await records.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        Assert.True(result.IsWritten);
        Assert.NotNull(result.Revision);
        return result.Revision;
    }

    #region WatchKeyAsync Tests

    [Fact]
    public async Task WatchKeyAsync_WhenTheLogCoversTheRevision_ReplaysWithoutAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        using var provider = new InProcessProvider(time);
        var records = GetService<IRecordService>(provider);
        var watches = GetService<IRecordWatchService>(provider);
        var key = InProcessTesting.Keyspace.CreateKey("a");
        var first = await CreateAsync(records, key, cancellationToken);
        var updated = "b"u8.ToArray();
        var swapped = await records.CompareAndSwapAsync(
            key,
            first,
            updated,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(swapped.IsWritten);
        Assert.NotNull(swapped.Revision);
        var status = await records.CompareAndDeleteAsync(
            key,
            swapped.Revision,
            null,
            cancellationToken
        );
        Assert.Equal(RecordDeleteStatus.Deleted, status);

        await using var stream = watches
            .WatchKeyAsync(key, first, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var moved = await stream.MoveNextAsync();

        Assert.True(moved);
        Assert.False(stream.Current.IsResync);
        Assert.Equal(RecordChangeKind.Deleted, stream.Current.Kind);
        Assert.Equal(key, stream.Current.Key);
        Assert.True(stream.Current.Revision > swapped.Revision);
    }

    [Fact]
    public async Task WatchKeyAsync_WhenTheRevisionIsBeforeTheLog_StartsWithAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        using var provider = new InProcessProvider(time);
        var records = GetService<IRecordService>(provider);
        var watches = GetService<IRecordWatchService>(provider);
        var key = InProcessTesting.Keyspace.CreateKey("a");
        var first = await CreateAsync(records, key, cancellationToken);
        var last = first;
        for (var index = 0; index <= InProcessChangeLog.Capacity; index++)
        {
            var other = InProcessTesting.Keyspace.CreateKey("other", $"k{index}");
            last = await CreateAsync(records, other, cancellationToken);
        }

        await using var stream = watches
            .WatchKeyAsync(key, first, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var moved = await stream.MoveNextAsync();

        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        Assert.Equal(last, stream.Current.Revision);
    }

    [Fact]
    public async Task WatchKeyAsync_WhenTheRevisionIsAhead_StartsWithAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        using var provider = new InProcessProvider(time);
        var records = GetService<IRecordService>(provider);
        var watches = GetService<IRecordWatchService>(provider);
        var key = InProcessTesting.Keyspace.CreateKey("a");
        var first = await CreateAsync(records, key, cancellationToken);
        var ahead = new Revision(first.Value + 100);

        await using var stream = watches
            .WatchKeyAsync(key, ahead, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var moved = await stream.MoveNextAsync();

        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        Assert.Equal(first, stream.Current.Revision);
    }

    #endregion

    #region WatchPrefixAsync Tests

    [Fact]
    public async Task WatchPrefixAsync_WhenTheSubscriberFallsBehind_YieldsAResync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        using var provider = new InProcessProvider(time);
        var records = GetService<IRecordService>(provider);
        var watches = GetService<IRecordWatchService>(provider);
        var prefix = InProcessTesting.Keyspace.CreateKey("cfg");
        await using var stream = watches
            .WatchPrefixAsync(prefix, null, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var moved = await stream.MoveNextAsync();
        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        var last = new Revision(1);
        for (var index = 0; index <= InProcessRecordWatcher.QueueCapacity; index++)
        {
            var key = InProcessTesting.Keyspace.CreateKey("cfg", $"k{index}");
            last = await CreateAsync(records, key, cancellationToken);
        }

        moved = await stream.MoveNextAsync();

        Assert.True(moved);
        Assert.True(stream.Current.IsResync);
        Assert.Equal(last, stream.Current.Revision);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenTheRecordExpires_ListsAtALaterRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        using var provider = new InProcessProvider(time);
        var records = GetService<IRecordService>(provider);
        var key = InProcessTesting.Keyspace.CreateKey("cfg", "a");
        var prefix = InProcessTesting.Keyspace.CreateKey("cfg");
        var page = new RecordPageRequest(10);
        var value = "a"u8.ToArray();
        var timeToLive = new TimeToLive(ShortTimeToLive);
        var lifetime = RecordLifetime.ForTimeToLive(timeToLive);
        var created = await records.CreateAsync(key, value, lifetime, null, cancellationToken);
        Assert.True(created.IsWritten);
        Assert.NotNull(created.Revision);
        var before = await records.ListAsync(prefix, page, null, cancellationToken);
        Assert.Single(before.Records);

        var margin = TimeSpan.FromSeconds(1);

        time.Advance(ShortTimeToLive + margin);

        var after = await records.ListAsync(prefix, page, null, cancellationToken);

        Assert.Empty(after.Records);
        Assert.True(after.Revision > created.Revision);
    }

    #endregion
}
