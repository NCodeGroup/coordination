// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the record primitive: a provider's test project derives a sealed class from it and overrides
/// <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class RecordContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's record service, and skips the test when the provider does not support records.
    /// </summary>
    private IRecordService Records => GetService<IRecordService>(Primitives.Record);

    /// <summary>
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    #region CreateAsync Tests

    /// <summary>
    /// Checks that creating a record under an absent key writes it and returns the revision a read gives.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenKeyIsAbsent_WritesTheRecord()
    {
        var key = CreateKey("a");
        var value = "one"u8.ToArray();

        var revision = await ContractRecords.CreateAsync(
            Records,
            key,
            value,
            TestCancellationToken
        );

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(value, actual);
        Assert.Equal(revision, record.Revision);
    }

    /// <summary>
    /// Checks that creating a record under a present key returns a conflict and keeps the stored value.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenKeyIsPresent_ReturnsConflict()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);

        var result = await Records.CreateAsync(
            key,
            two,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.Equal(RecordWriteStatus.Conflict, result.Status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(one, actual);
    }

    /// <summary>
    /// Checks that creating a record whose value has the largest length writes it.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenValueHasTheLargestLength_WritesTheRecord()
    {
        var key = CreateKey("a");
        var value = new byte[CoordinationLimits.MaxValueLength];

        await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal(CoordinationLimits.MaxValueLength, record.Value.Length);
    }

    /// <summary>
    /// Checks that creating a record whose value is longer than the limit throws and writes nothing.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenValueIsTooLong_ThrowsArgumentException()
    {
        var key = CreateKey("a");
        var value = new byte[CoordinationLimits.MaxValueLength + 1];

        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await Records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                TestCancellationToken
            )
        );

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(record);
    }

    /// <summary>
    /// Checks that a record written after another record in the same keyspace has a later revision.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenAnotherKeyIsWritten_GivesALaterRevision()
    {
        var firstKey = CreateKey("a");
        var secondKey = CreateKey("b");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var first = await ContractRecords.CreateAsync(
            Records,
            firstKey,
            one,
            TestCancellationToken
        );

        var second = await ContractRecords.CreateAsync(
            Records,
            secondKey,
            two,
            TestCancellationToken
        );

        Assert.True(second > first);
    }

    /// <summary>
    /// Checks that a record deleted after it was written can be created again, at a later revision.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTheRecordWasDeleted_WritesItAgain()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var first = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);
        var deleted = await Records.CompareAndDeleteAsync(key, first, null, TestCancellationToken);
        Assert.Equal(RecordDeleteStatus.Deleted, deleted);

        var second = await ContractRecords.CreateAsync(Records, key, two, TestCancellationToken);

        Assert.True(second > first);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(two, actual);
    }

    /// <summary>
    /// Checks that a record created with a time to live is present while the time to live runs, is gone once it has
    /// passed, and can then be created again.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTimeToLivePasses_ExpiresTheRecord()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        await ContractRecords.CreateAsync(Records, key, one, lifetime, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var alive = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(alive);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var expired = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(expired);
        await ContractRecords.CreateAsync(Records, key, two, TestCancellationToken);
    }

    /// <summary>
    /// Checks that creating a record with a token that is already canceled throws an operation canceled exception.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var key = CreateKey("a");
        var value = "one"u8.ToArray();
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Records.CreateAsync(key, value, RecordLifetime.Persistent, null, canceledToken)
        );
    }

    /// <summary>
    /// Checks that creating a record bound to a live session writes it and returns the revision a read gives.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenBoundToALiveSession_WritesTheRecord()
    {
        var key = CreateKey("a");
        var value = "one"u8.ToArray();
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);

        var revision = await ContractRecords.CreateAsync(
            Records,
            key,
            value,
            lifetime,
            TestCancellationToken
        );

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(value, actual);
        Assert.Equal(revision, record.Revision);
    }

    /// <summary>
    /// Checks that closing a session deletes the records bound to it and lets the key be created again.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTheSessionCloses_DeletesTheRecord()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);
        await ContractRecords.CreateAsync(Records, key, one, lifetime, TestCancellationToken);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(record);
        await ContractRecords.CreateAsync(Records, key, two, TestCancellationToken);
    }

    /// <summary>
    /// Checks that a record bound to a session is present while the session's time to live runs and is deleted once
    /// the session has expired.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTheSessionExpires_DeletesTheRecord()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            ShortTimeToLive,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);
        await ContractRecords.CreateAsync(Records, key, one, lifetime, TestCancellationToken);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var alive = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(alive);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var expired = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(expired);
    }

    /// <summary>
    /// Checks that creating a record bound to a session that has ended returns a session ended result and writes
    /// nothing.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTheSessionHasEnded_ReturnsSessionEnded()
    {
        var key = CreateKey("a");
        var value = "one"u8.ToArray();
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);

        var result = await Records.CreateAsync(key, value, lifetime, null, TestCancellationToken);

        Assert.False(result.IsWritten);
        Assert.Equal(RecordWriteStatus.SessionEnded, result.Status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(record);
    }

    #endregion

    #region ReadAsync Tests

    /// <summary>
    /// Checks that reading an absent key returns null.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenKeyIsAbsent_ReturnsNull()
    {
        var key = CreateKey("absent");

        var record = await Records.ReadAsync(key, null, TestCancellationToken);

        Assert.Null(record);
    }

    /// <summary>
    /// Checks that reading with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var key = CreateKey("a");

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Records.ReadAsync(key, UnknownExtension, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region CompareAndSwapAsync Tests

    /// <summary>
    /// Checks that swapping a record at its current revision writes the new value at a later revision.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenRevisionMatches_WritesALaterRevision()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var created = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);

        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            two,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.True(result.IsWritten);
        Assert.True(result.Revision > created);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(two, actual);
        Assert.Equal(result.Revision, record.Revision);
    }

    /// <summary>
    /// Checks that swapping a record at a revision that is not its current one returns a conflict and keeps the
    /// stored value.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenRevisionDiffers_ReturnsConflict()
    {
        var key = CreateKey("a");
        var otherKey = CreateKey("b");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var three = "three"u8.ToArray();
        var created = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);
        var later = await ContractRecords.CreateAsync(
            Records,
            otherKey,
            two,
            TestCancellationToken
        );
        Assert.NotEqual(created, later);

        var result = await Records.CompareAndSwapAsync(
            key,
            later,
            three,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.Equal(RecordWriteStatus.Conflict, result.Status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(one, actual);
        Assert.Equal(created, record.Revision);
    }

    /// <summary>
    /// Checks that swapping a record under an absent key returns not found.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenKeyIsAbsent_ReturnsNotFound()
    {
        var key = CreateKey("absent");
        var revision = new Revision(1);
        var value = "one"u8.ToArray();

        var result = await Records.CompareAndSwapAsync(
            key,
            revision,
            value,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.Equal(RecordWriteStatus.NotFound, result.Status);
    }

    /// <summary>
    /// Checks that a swap that carries a time to live measures it from the swap, not from the earlier write.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenItCarriesATimeToLive_MeasuresItFromTheWrite()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        var created = await ContractRecords.CreateAsync(
            Records,
            key,
            one,
            lifetime,
            TestCancellationToken
        );
        var beforeSwap = TimeSpan.FromSeconds(1.5);
        await AdvanceClockAsync(beforeSwap, TestCancellationToken);

        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            two,
            lifetime,
            null,
            TestCancellationToken
        );
        Assert.True(result.IsWritten);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(two, actual);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);

        var expired = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(expired);
    }

    /// <summary>
    /// Checks that a swap with a persistent lifetime keeps the record past the time to live of the write it replaced.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenLifetimeIsPersistent_KeepsTheRecordPastTheEarlierTimeToLive()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        var created = await ContractRecords.CreateAsync(
            Records,
            key,
            one,
            lifetime,
            TestCancellationToken
        );

        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            two,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );
        Assert.True(result.IsWritten);

        var pastTimeToLive = TimeSpan.FromSeconds(3);
        await AdvanceClockAsync(pastTimeToLive, TestCancellationToken);

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(two, actual);
    }

    /// <summary>
    /// Checks that swapping a record with a lifetime bound to a session that has ended returns a session ended
    /// result and keeps the stored value.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenTheSessionHasEnded_ReturnsSessionEnded()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var created = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);

        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            two,
            lifetime,
            null,
            TestCancellationToken
        );

        Assert.False(result.IsWritten);
        Assert.Equal(RecordWriteStatus.SessionEnded, result.Status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(one, actual);
        Assert.Equal(created, record.Revision);
    }

    /// <summary>
    /// Checks that a swap whose lifetime is persistent takes the record off its session, so the record stays after
    /// the session ends.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenTheLifetimeLeavesTheSession_KeepsTheRecordAfterTheSessionEnds()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var bound = RecordLifetime.ForSession(session);
        var created = await ContractRecords.CreateAsync(
            Records,
            key,
            one,
            bound,
            TestCancellationToken
        );
        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            two,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );
        Assert.True(result.IsWritten);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(two, actual);
    }

    #endregion

    #region CompareAndDeleteAsync Tests

    /// <summary>
    /// Checks that deleting a record at its current revision removes it.
    /// </summary>
    [Fact]
    public async Task CompareAndDeleteAsync_WhenRevisionMatches_DeletesTheRecord()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var created = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);

        var status = await Records.CompareAndDeleteAsync(key, created, null, TestCancellationToken);

        Assert.Equal(RecordDeleteStatus.Deleted, status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(record);
    }

    /// <summary>
    /// Checks that deleting a record at a revision that is not its current one returns a conflict and keeps the
    /// record.
    /// </summary>
    [Fact]
    public async Task CompareAndDeleteAsync_WhenRevisionDiffers_ReturnsConflict()
    {
        var key = CreateKey("a");
        var otherKey = CreateKey("b");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var created = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);
        var later = await ContractRecords.CreateAsync(
            Records,
            otherKey,
            two,
            TestCancellationToken
        );
        Assert.NotEqual(created, later);

        var status = await Records.CompareAndDeleteAsync(key, later, null, TestCancellationToken);

        Assert.Equal(RecordDeleteStatus.Conflict, status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        var actual = record.Value.ToArray();
        Assert.Equal(one, actual);
    }

    /// <summary>
    /// Checks that deleting a record under an absent key returns not found.
    /// </summary>
    [Fact]
    public async Task CompareAndDeleteAsync_WhenKeyIsAbsent_ReturnsNotFound()
    {
        var key = CreateKey("absent");
        var revision = new Revision(1);

        var status = await Records.CompareAndDeleteAsync(
            key,
            revision,
            null,
            TestCancellationToken
        );

        Assert.Equal(RecordDeleteStatus.NotFound, status);
    }

    #endregion

    #region TakeAsync Tests

    /// <summary>
    /// Checks that taking a present record returns it and deletes it.
    /// </summary>
    [Fact]
    public async Task TakeAsync_WhenKeyIsPresent_ReturnsAndDeletesTheRecord()
    {
        var key = CreateKey("a");
        var one = "one"u8.ToArray();
        var created = await ContractRecords.CreateAsync(Records, key, one, TestCancellationToken);

        var taken = await Records.TakeAsync(key, null, TestCancellationToken);

        Assert.NotNull(taken);
        var actual = taken.Value.ToArray();
        Assert.Equal(one, actual);
        Assert.Equal(created, taken.Revision);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.Null(record);
    }

    /// <summary>
    /// Checks that taking an absent key returns null.
    /// </summary>
    [Fact]
    public async Task TakeAsync_WhenKeyIsAbsent_ReturnsNull()
    {
        var key = CreateKey("absent");

        var taken = await Records.TakeAsync(key, null, TestCancellationToken);

        Assert.Null(taken);
    }

    #endregion

    #region ListAsync Tests

    /// <summary>
    /// Checks that listing a prefix returns the prefix's own record and the records below it, and no others.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenRecordsAreUnderThePrefix_ReturnsThemWithThePrefixesOwnRecord()
    {
        var prefix = CreateKey("cfg");
        var childA = CreateKey("cfg", "a");
        var childB = CreateKey("cfg", "b");
        var otherKey = CreateKey("other");
        var value = "v"u8.ToArray();
        var keys = new[] { prefix, childA, childB };
        foreach (var key in keys)
        {
            await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);
        }

        await ContractRecords.CreateAsync(Records, otherKey, value, TestCancellationToken);
        var request = new RecordPageRequest(10);

        var page = await Records.ListAsync(prefix, request, null, TestCancellationToken);

        Assert.Equal(keys.Length, page.Records.Length);
        foreach (var key in keys)
        {
            Assert.Contains(page.Records, record => record.Key.Equals(key));
        }

        Assert.Null(page.ContinuationToken);
        foreach (var record in page.Records)
        {
            Assert.True(page.Revision >= record.Revision);
        }
    }

    /// <summary>
    /// Checks that listing with a limit below the number of records pages through every record once.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenRecordsExceedTheLimit_PagesThroughEachRecord()
    {
        const int count = 5;
        const int limit = 2;
        var value = "v"u8.ToArray();
        var prefix = CreateKey("cfg");
        var keys = new List<CoordinationKey>();
        for (var index = 0; index < count; index++)
        {
            var key = CreateKey("cfg", index);
            keys.Add(key);
            await ContractRecords.CreateAsync(Records, key, value, TestCancellationToken);
        }

        var seen = new List<CoordinationKey>();
        string? token = null;
        var pages = 0;
        do
        {
            var request = new RecordPageRequest(limit) { ContinuationToken = token };

            var page = await Records.ListAsync(prefix, request, null, TestCancellationToken);

            Assert.True(page.Records.Length <= limit);
            var pageKeys = page.Records.Select(record => record.Key);
            seen.AddRange(pageKeys);
            token = page.ContinuationToken;
            pages++;
            Assert.True(pages <= count + 1, "The listing did not end.");
        } while (token is not null);

        Assert.Equal(count, seen.Count);
        foreach (var key in keys)
        {
            Assert.Single(seen, seenKey => seenKey.Equals(key));
        }
    }

    /// <summary>
    /// Checks that listing a prefix with no records under it returns an empty page without a continuation token.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenNothingIsUnderThePrefix_ReturnsAnEmptyPage()
    {
        var otherKey = CreateKey("other");
        var prefix = CreateKey("cfg");
        var value = "v"u8.ToArray();
        var request = new RecordPageRequest(10);
        await ContractRecords.CreateAsync(Records, otherKey, value, TestCancellationToken);

        var page = await Records.ListAsync(prefix, request, null, TestCancellationToken);

        Assert.Empty(page.Records);
        Assert.Null(page.ContinuationToken);
    }

    /// <summary>
    /// Checks that listing a prefix leaves out a record whose time to live has passed.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenARecordExpired_LeavesItOut()
    {
        var expiring = CreateKey("cfg", "a");
        var kept = CreateKey("cfg", "b");
        var prefix = CreateKey("cfg");
        var value = "v"u8.ToArray();
        var request = new RecordPageRequest(10);
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        await ContractRecords.CreateAsync(
            Records,
            expiring,
            value,
            lifetime,
            TestCancellationToken
        );
        await ContractRecords.CreateAsync(Records, kept, value, TestCancellationToken);

        await AdvanceClockAsync(StillAlive + PastExpiry, TestCancellationToken);

        var page = await Records.ListAsync(prefix, request, null, TestCancellationToken);
        var record = Assert.Single(page.Records);
        Assert.Equal(kept, record.Key);
    }

    /// <summary>
    /// Checks that listing a prefix returns a record that was replaced once, with its new value.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenARecordWasReplaced_ListsItOnceWithItsNewValue()
    {
        var replaced = CreateKey("cfg", "a");
        var other = CreateKey("cfg", "b");
        var prefix = CreateKey("cfg");
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var request = new RecordPageRequest(10);
        var created = await ContractRecords.CreateAsync(
            Records,
            replaced,
            one,
            TestCancellationToken
        );
        await ContractRecords.CreateAsync(Records, other, one, TestCancellationToken);
        await Records.CompareAndSwapAsync(
            replaced,
            created,
            two,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        var page = await Records.ListAsync(prefix, request, null, TestCancellationToken);

        Assert.Equal(2, page.Records.Length);
        var listedReplaced = Assert.Single(page.Records, record => record.Key.Equals(replaced));
        var actual = listedReplaced.Value.ToArray();
        Assert.Equal(two, actual);
        Assert.Single(page.Records, record => record.Key.Equals(other));
    }

    /// <summary>
    /// Checks that listing a prefix leaves out a record that was deleted and lists it once again when it is created
    /// again.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenARecordWasDeleted_LeavesItOut()
    {
        var deleted = CreateKey("cfg", "a");
        var kept = CreateKey("cfg", "b");
        var prefix = CreateKey("cfg");
        var value = "v"u8.ToArray();
        var request = new RecordPageRequest(10);
        var created = await ContractRecords.CreateAsync(
            Records,
            deleted,
            value,
            TestCancellationToken
        );
        await ContractRecords.CreateAsync(Records, kept, value, TestCancellationToken);
        await Records.CompareAndDeleteAsync(deleted, created, null, TestCancellationToken);

        var afterDelete = await Records.ListAsync(prefix, request, null, TestCancellationToken);

        var remaining = Assert.Single(afterDelete.Records);
        Assert.Equal(kept, remaining.Key);

        await ContractRecords.CreateAsync(Records, deleted, value, TestCancellationToken);

        var afterCreate = await Records.ListAsync(prefix, request, null, TestCancellationToken);

        Assert.Equal(2, afterCreate.Records.Length);
        Assert.Single(afterCreate.Records, record => record.Key.Equals(deleted));
        Assert.Single(afterCreate.Records, record => record.Key.Equals(kept));
    }

    /// <summary>
    /// Checks that listing a prefix leaves out the records bound to a session that has closed.
    /// </summary>
    [Fact]
    public async Task ListAsync_WhenTheSessionCloses_LeavesItsRecordsOut()
    {
        var bound = CreateKey("cfg", "a");
        var kept = CreateKey("cfg", "b");
        var prefix = CreateKey("cfg");
        var value = "v"u8.ToArray();
        var request = new RecordPageRequest(10);
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);
        await ContractRecords.CreateAsync(Records, bound, value, lifetime, TestCancellationToken);
        await ContractRecords.CreateAsync(Records, kept, value, TestCancellationToken);
        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var page = await Records.ListAsync(prefix, request, null, TestCancellationToken);

        var record = Assert.Single(page.Records);
        Assert.Equal(kept, record.Key);
    }

    #endregion
}
