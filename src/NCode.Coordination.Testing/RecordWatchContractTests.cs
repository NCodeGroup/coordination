// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the record watch primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
/// <remarks>
/// A provider registers a watch before it yields the watch's first item, so a test changes records after it reads
/// that item and expects the change. A watch may skip a key's intermediate values, so a test that follows a watch
/// applies its changes until its view reaches the revision it expects and compares the view with the store.
/// </remarks>
public abstract class RecordWatchContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's record watch service, and skips the test when the provider does not support record watches.
    /// </summary>
    private IRecordWatchService Watches => GetService<IRecordWatchService>(Primitives.RecordWatch);

    /// <summary>
    /// Gets the provider's record service, and skips the test when the provider does not support records.
    /// </summary>
    private IRecordService Records => GetService<IRecordService>(Primitives.Record);

    /// <summary>
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    /// <summary>
    /// Replaces a record that holds a revision with a persistent value and returns the revision of the write.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="expected">The revision the record holds.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The revision of the write.</returns>
    private async ValueTask<Revision> SwapAsync(
        CoordinationKey key,
        Revision expected,
        byte[] value
    )
    {
        var result = await Records.CompareAndSwapAsync(
            key,
            expected,
            value,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.True(result.IsWritten);
        Assert.NotNull(result.Revision);
        return result.Revision;
    }

    /// <summary>
    /// Lists every record under a prefix that a filter keeps, page by page.
    /// </summary>
    /// <param name="prefix">The prefix to list.</param>
    /// <param name="matches">The filter that tells the keys to keep.</param>
    /// <returns>The records by key, and the revision that the last page reflects.</returns>
    private async ValueTask<(
        Dictionary<CoordinationKey, StoredRecord> Records,
        Revision Revision
    )> ListAllAsync(CoordinationKey prefix, Func<CoordinationKey, bool> matches)
    {
        const int limit = 100;
        var view = new Dictionary<CoordinationKey, StoredRecord>();
        string? token = null;
        Revision revision;
        do
        {
            var request = new RecordPageRequest(limit) { ContinuationToken = token };

            var page = await Records.ListAsync(prefix, request, null, TestCancellationToken);

            foreach (var record in page.Records)
            {
                if (matches(record.Key))
                {
                    view[record.Key] = record;
                }
            }

            revision = page.Revision;
            token = page.ContinuationToken;
        } while (token is not null);

        return (view, revision);
    }

    /// <summary>
    /// Applies the changes of a watch, from a first change, until the view reaches a revision.
    /// </summary>
    /// <param name="changes">The enumerator that reads the watch.</param>
    /// <param name="first">The change to start with, which the test has read.</param>
    /// <param name="prefix">The prefix that a resync lists.</param>
    /// <param name="matches">The filter that tells the keys the watch covers.</param>
    /// <param name="until">The revision that the view has to reach.</param>
    /// <returns>The view of the watched records at the revision it reached, by key.</returns>
    private async ValueTask<Dictionary<CoordinationKey, StoredRecord>> ConvergeAsync(
        IAsyncEnumerator<RecordChange> changes,
        RecordChange first,
        CoordinationKey prefix,
        Func<CoordinationKey, bool> matches,
        Revision until
    )
    {
        var view = new Dictionary<CoordinationKey, StoredRecord>();
        var change = first;
        while (true)
        {
            if (change.IsResync)
            {
                var (listed, revision) = await ListAllAsync(prefix, matches);
                view = listed;
                if (revision >= until)
                {
                    return view;
                }
            }
            else
            {
                if (change.Kind == RecordChangeKind.Written)
                {
                    Assert.NotNull(change.Record);
                    view[change.Key] = change.Record;
                }
                else
                {
                    view.Remove(change.Key);
                }

                if (change.Revision >= until)
                {
                    return view;
                }
            }

            change = await ContractStreams.NextAsync(changes);
        }
    }

    #region WatchKeyAsync Tests

    /// <summary>
    /// Checks that a watch that starts without a revision begins with a resync.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenStartedWithoutARevision_YieldsAResyncFirst()
    {
        var key = CreateKey("a");

        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, null, null, TestCancellationToken)
        );
        var first = await ContractStreams.NextAsync(stream);

        Assert.True(first.IsResync);
    }

    /// <summary>
    /// Checks that a watch yields a record that is written after the watch started, with its key, value and revision.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenTheRecordIsWritten_YieldsTheWrite()
    {
        var key = CreateKey("a");
        var value = "one"u8.ToArray();
        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, null, null, TestCancellationToken)
        );
        await ContractStreams.NextAsync(stream);

        var revision = await ContractRecords.CreateAsync(
            Records,
            key,
            value,
            TestCancellationToken
        );

        var change = await ContractStreams.NextMatchingAsync(stream, item => !item.IsResync);
        Assert.Equal(RecordChangeKind.Written, change.Kind);
        Assert.Equal(key, change.Key);
        Assert.Equal(revision, change.Revision);
        Assert.NotNull(change.Record);
        Assert.Equal(value, change.Record.Value.ToArray());
        Assert.Equal(revision, change.Record.Revision);
    }

    /// <summary>
    /// Checks that a watch yields the deletion of its record at a revision after the write's.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenTheRecordIsDeleted_YieldsTheDelete()
    {
        var key = CreateKey("a");
        var written = await ContractRecords.CreateAsync(
            Records,
            key,
            "one"u8.ToArray(),
            TestCancellationToken
        );
        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, null, null, TestCancellationToken)
        );
        await ContractStreams.NextAsync(stream);

        var status = await Records.CompareAndDeleteAsync(key, written, null, TestCancellationToken);

        Assert.Equal(RecordDeleteStatus.Deleted, status);

        var change = await ContractStreams.NextMatchingAsync(stream, item => !item.IsResync);
        Assert.Equal(RecordChangeKind.Deleted, change.Kind);
        Assert.Equal(key, change.Key);
        Assert.True(change.Revision > written);
    }

    /// <summary>
    /// Checks that a watch of one key yields nothing for a write to another key.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenAnotherKeyIsWritten_YieldsNothingForIt()
    {
        var key = CreateKey("a");
        var otherKey = CreateKey("b");
        var value = "one"u8.ToArray();
        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, null, null, TestCancellationToken)
        );
        await ContractStreams.NextAsync(stream);

        await ContractRecords.CreateAsync(Records, otherKey, value, TestCancellationToken);
        await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(stream, item => !item.IsResync);
        Assert.Equal(RecordChangeKind.Written, change.Kind);
        Assert.Equal(key, change.Key);
    }

    /// <summary>
    /// Checks that a watch yields a deletion when its record's time to live passes.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenTheRecordExpires_YieldsADelete()
    {
        var key = CreateKey("a");
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        await ContractRecords.CreateAsync(
            Records,
            key,
            "one"u8.ToArray(),
            lifetime,
            TestCancellationToken
        );
        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, null, null, TestCancellationToken)
        );
        await ContractStreams.NextAsync(stream);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(stream, item =>
            item.Kind == RecordChangeKind.Deleted
        );
        Assert.Equal(key, change.Key);
    }

    /// <summary>
    /// Checks that a watch yields a deletion when the session that its record is bound to ends.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenTheRecordsSessionEnds_YieldsADelete()
    {
        var key = CreateKey("a");
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        await ContractRecords.CreateAsync(
            Records,
            key,
            "one"u8.ToArray(),
            RecordLifetime.ForSession(session),
            TestCancellationToken
        );
        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, null, null, TestCancellationToken)
        );
        await ContractStreams.NextAsync(stream);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(stream, item =>
            item.Kind == RecordChangeKind.Deleted
        );
        Assert.Equal(key, change.Key);
    }

    /// <summary>
    /// Checks that a watch yields the changes of a record in increasing revision order.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenChangesFollow_YieldsThemInRevisionOrder()
    {
        var key = CreateKey("a");
        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, null, null, TestCancellationToken)
        );
        await ContractStreams.NextAsync(stream);
        var first = await ContractRecords.CreateAsync(
            Records,
            key,
            "one"u8.ToArray(),
            TestCancellationToken
        );
        var second = await SwapAsync(key, first, "two"u8.ToArray());
        var third = await SwapAsync(key, second, "three"u8.ToArray());

        Revision? previous = null;
        while (previous is null || previous < third)
        {
            var change = await ContractStreams.NextMatchingAsync(stream, item => !item.IsResync);

            Assert.True(previous is null || change.Revision > previous);
            previous = change.Revision;
        }

        Assert.Equal(third, previous);
    }

    /// <summary>
    /// Checks that a watch that resumes after a revision brings a view that equals the store once it reaches the
    /// revision of the latest write.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenResumedAfterARevision_ConvergesOnTheStore()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var first = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);
        var second = await SwapAsync(key, first, two);

        await using var stream = ContractStreams.Open(
            Watches.WatchKeyAsync(key, first, null, TestCancellationToken)
        );
        var change = await ContractStreams.NextAsync(stream);

        Assert.True(
            change.IsResync
                || (change.Kind == RecordChangeKind.Written && change.Revision == second)
        );

        var view = await ConvergeAsync(stream, change, key, item => item.Equals(key), second);

        var record = Assert.Single(view).Value;
        Assert.Equal(key, record.Key);
        Assert.Equal(second, record.Revision);
        Assert.Equal(two, record.Value.ToArray());
    }

    /// <summary>
    /// Checks that watching with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var key = CreateKey("a");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
        {
            await using var stream = Watches
                .WatchKeyAsync(key, null, UnknownExtension, TestCancellationToken)
                .GetAsyncEnumerator(TestCancellationToken);
            await stream.MoveNextAsync();
        });

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region WatchPrefixAsync Tests

    /// <summary>
    /// Checks that a watch of a prefix brings a view that equals the listing of the prefix once it reaches the
    /// revision of the latest write.
    /// </summary>
    [Fact]
    public async Task WatchPrefixAsync_WhenRecordsChange_ConvergesOnTheStore()
    {
        var prefix = CreateKey("cfg");
        var keyA = CreateKey("cfg", "a");
        var keyB = CreateKey("cfg", "b");
        var keyC = CreateKey("cfg", "c");
        var other = CreateKey("other");
        await using var stream = ContractStreams.Open(
            Watches.WatchPrefixAsync(prefix, null, null, TestCancellationToken)
        );
        var first = await ContractStreams.NextAsync(stream);
        var createdA = await ContractRecords.CreateAsync(
            Records,
            keyA,
            "a1"u8.ToArray(),
            TestCancellationToken
        );
        await ContractRecords.CreateAsync(Records, keyB, "b1"u8.ToArray(), TestCancellationToken);
        await SwapAsync(keyA, createdA, "a2"u8.ToArray());
        await Records.TakeAsync(keyB, null, TestCancellationToken);
        await ContractRecords.CreateAsync(Records, other, "o1"u8.ToArray(), TestCancellationToken);
        var createdC = await ContractRecords.CreateAsync(
            Records,
            keyC,
            "c1"u8.ToArray(),
            TestCancellationToken
        );

        var view = await ConvergeAsync(stream, first, prefix, _ => true, createdC);

        var (listed, _) = await ListAllAsync(prefix, _ => true);
        Assert.Equal(2, listed.Count);
        Assert.Equal(listed.Count, view.Count);
        foreach (var (key, expected) in listed)
        {
            Assert.True(view.TryGetValue(key, out var actual));
            Assert.Equal(expected.Revision, actual.Revision);
            Assert.Equal(expected.Value.ToArray(), actual.Value.ToArray());
        }

        Assert.Equal("a2"u8.ToArray(), listed[keyA].Value.ToArray());
        Assert.Equal("c1"u8.ToArray(), listed[keyC].Value.ToArray());
    }

    /// <summary>
    /// Checks that a watch of a prefix yields nothing for a write to a key outside the prefix.
    /// </summary>
    [Fact]
    public async Task WatchPrefixAsync_WhenAKeyIsOutsideThePrefix_YieldsNothingForIt()
    {
        var prefix = CreateKey("cfg");
        var key = CreateKey("cfg", "a");
        var other = CreateKey("other");
        var value = "one"u8.ToArray();
        await using var stream = ContractStreams.Open(
            Watches.WatchPrefixAsync(prefix, null, null, TestCancellationToken)
        );
        await ContractStreams.NextAsync(stream);

        await ContractRecords.CreateAsync(Records, other, value, TestCancellationToken);
        await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(stream, item => !item.IsResync);
        Assert.Equal(RecordChangeKind.Written, change.Kind);
        Assert.Equal(key, change.Key);
    }

    #endregion
}
