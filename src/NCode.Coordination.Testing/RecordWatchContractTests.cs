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
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The revision of the write.</returns>
    private async ValueTask<Revision> SwapAsync(
        CoordinationKey key,
        Revision expected,
        byte[] value,
        CancellationToken cancellationToken
    )
    {
        var result = await Records.CompareAndSwapAsync(
            key,
            expected,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        Assert.True(result.IsWritten);
        Assert.NotNull(result.Revision);
        return result.Revision;
    }

    /// <summary>
    /// Lists every record that a watch's scope covers, page by page.
    /// </summary>
    /// <param name="scope">The records the watch covers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The records by key, and the revision that the last page reflects.</returns>
    private async ValueTask<(
        Dictionary<CoordinationKey, StoredRecord> Records,
        Revision Revision
    )> ListAllAsync(ContractWatchScope scope, CancellationToken cancellationToken)
    {
        const int limit = 100;
        var view = new Dictionary<CoordinationKey, StoredRecord>();
        string? token = null;
        Revision revision;
        do
        {
            var request = new RecordPageRequest(limit) { ContinuationToken = token };

            var page = await Records.ListAsync(scope.Prefix, request, null, cancellationToken);

            foreach (var record in page.Records)
            {
                if (scope.Matches(record.Key))
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
    /// <param name="scope">The records the watch covers, which a resync lists.</param>
    /// <param name="until">The revision that the view has to reach.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The view of the watched records at the revision it reached, by key.</returns>
    private async ValueTask<Dictionary<CoordinationKey, StoredRecord>> ConvergeAsync(
        IAsyncEnumerator<RecordChange> changes,
        RecordChange first,
        ContractWatchScope scope,
        Revision until,
        CancellationToken cancellationToken
    )
    {
        var view = new Dictionary<CoordinationKey, StoredRecord>();
        var change = first;
        while (true)
        {
            if (change.IsResync)
            {
                var (listed, revision) = await ListAllAsync(scope, cancellationToken);
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

            change = await ContractStreams.NextAsync(changes, cancellationToken);
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

        var watch = Watches.WatchKeyAsync(key, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        var first = await ContractStreams.NextAsync(stream, TestCancellationToken);

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
        var watch = Watches.WatchKeyAsync(key, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        var revision = await ContractRecords.CreateAsync(
            Records,
            key,
            value,
            TestCancellationToken
        );

        var change = await ContractStreams.NextMatchingAsync(
            stream,
            item => !item.IsResync,
            TestCancellationToken
        );
        Assert.Equal(RecordChangeKind.Written, change.Kind);
        Assert.Equal(key, change.Key);
        Assert.Equal(revision, change.Revision);
        Assert.NotNull(change.Record);
        var written = change.Record.Value.ToArray();
        Assert.Equal(value, written);
        Assert.Equal(revision, change.Record.Revision);
    }

    /// <summary>
    /// Checks that a watch yields the deletion of its record at a revision after the write's.
    /// </summary>
    [Fact]
    public async Task WatchKeyAsync_WhenTheRecordIsDeleted_YieldsTheDelete()
    {
        var key = CreateKey("a");
        var value = "one"u8.ToArray();
        var written = await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);
        var watch = Watches.WatchKeyAsync(key, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        var status = await Records.CompareAndDeleteAsync(key, written, null, TestCancellationToken);

        Assert.Equal(RecordDeleteStatus.Deleted, status);

        var change = await ContractStreams.NextMatchingAsync(
            stream,
            item => !item.IsResync,
            TestCancellationToken
        );
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
        var watch = Watches.WatchKeyAsync(key, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        await ContractRecords.CreateAsync(Records, otherKey, value, TestCancellationToken);
        await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(
            stream,
            item => !item.IsResync,
            TestCancellationToken
        );
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
        var value = "one"u8.ToArray();
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        await ContractRecords.CreateAsync(Records, key, value, lifetime, TestCancellationToken);
        var watch = Watches.WatchKeyAsync(key, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(
            stream,
            item => item.Kind == RecordChangeKind.Deleted,
            TestCancellationToken
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
        var value = "one"u8.ToArray();
        var lifetime = RecordLifetime.ForSession(session);
        await ContractRecords.CreateAsync(Records, key, value, lifetime, TestCancellationToken);
        var watch = Watches.WatchKeyAsync(key, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(
            stream,
            item => item.Kind == RecordChangeKind.Deleted,
            TestCancellationToken
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
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var three = "three"u8.ToArray();
        var watch = Watches.WatchKeyAsync(key, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);
        var first = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);
        var second = await SwapAsync(key, first, two, TestCancellationToken);
        var third = await SwapAsync(key, second, three, TestCancellationToken);

        Revision? previous = null;
        while (previous is null || previous < third)
        {
            var change = await ContractStreams.NextMatchingAsync(
                stream,
                item => !item.IsResync,
                TestCancellationToken
            );

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
        var second = await SwapAsync(key, first, two, TestCancellationToken);

        var watch = Watches.WatchKeyAsync(key, first, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        var change = await ContractStreams.NextAsync(stream, TestCancellationToken);

        Assert.True(
            change.IsResync
                || (change.Kind == RecordChangeKind.Written && change.Revision == second)
        );

        var scope = new ContractWatchScope(key, item => item.Equals(key));

        var view = await ConvergeAsync(stream, change, scope, second, TestCancellationToken);

        var record = Assert.Single(view).Value;
        Assert.Equal(key, record.Key);
        var actual = record.Value.ToArray();
        Assert.Equal(second, record.Revision);
        Assert.Equal(two, actual);
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
        var valueA1 = "a1"u8.ToArray();
        var valueA2 = "a2"u8.ToArray();
        var valueB1 = "b1"u8.ToArray();
        var valueC1 = "c1"u8.ToArray();
        var valueO1 = "o1"u8.ToArray();
        var watch = Watches.WatchPrefixAsync(prefix, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        var first = await ContractStreams.NextAsync(stream, TestCancellationToken);
        var createdA = await ContractRecords.CreateAsync(
            Records,
            keyA,
            valueA1,
            TestCancellationToken
        );
        await ContractRecords.CreateAsync(Records, keyB, valueB1, TestCancellationToken);
        await SwapAsync(keyA, createdA, valueA2, TestCancellationToken);
        await Records.TakeAsync(keyB, null, TestCancellationToken);
        await ContractRecords.CreateAsync(Records, other, valueO1, TestCancellationToken);
        var createdC = await ContractRecords.CreateAsync(
            Records,
            keyC,
            valueC1,
            TestCancellationToken
        );

        var scope = new ContractWatchScope(prefix, _ => true);

        var view = await ConvergeAsync(stream, first, scope, createdC, TestCancellationToken);

        var (listed, _) = await ListAllAsync(scope, TestCancellationToken);
        Assert.Equal(2, listed.Count);
        Assert.Equal(listed.Count, view.Count);
        foreach (var (key, expected) in listed)
        {
            var found = view.TryGetValue(key, out var actual);
            Assert.True(found);
            Assert.NotNull(actual);
            var expectedValue = expected.Value.ToArray();
            var actualValue = actual.Value.ToArray();
            Assert.Equal(expected.Revision, actual.Revision);
            Assert.Equal(expectedValue, actualValue);
        }

        var listedA = listed[keyA].Value.ToArray();
        var listedC = listed[keyC].Value.ToArray();
        Assert.Equal(valueA2, listedA);
        Assert.Equal(valueC1, listedC);
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
        var watch = Watches.WatchPrefixAsync(prefix, null, null, TestCancellationToken);
        await using var stream = ContractStreams.Open(watch, TestCancellationToken);
        await ContractStreams.NextAsync(stream, TestCancellationToken);

        await ContractRecords.CreateAsync(Records, other, value, TestCancellationToken);
        await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);

        var change = await ContractStreams.NextMatchingAsync(
            stream,
            item => !item.IsResync,
            TestCancellationToken
        );
        Assert.Equal(RecordChangeKind.Written, change.Kind);
        Assert.Equal(key, change.Key);
    }

    #endregion
}
