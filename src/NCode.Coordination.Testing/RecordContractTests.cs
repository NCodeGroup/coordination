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
    /// Creates a persistent record that must not exist yet and returns the revision it was written at.
    /// </summary>
    /// <param name="key">The key to create.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The revision of the record.</returns>
    private async ValueTask<Revision> CreateRecordAsync(CoordinationKey key, byte[] value)
    {
        var result = await Records.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.True(result.IsWritten);
        return result.Revision;
    }

    /// <summary>
    /// Creates a record that expires after <see cref="ProviderContractTests.ShortTimeToLive"/> and returns the
    /// revision it was written at.
    /// </summary>
    /// <param name="key">The key to create.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The revision of the record.</returns>
    private async ValueTask<Revision> CreateExpiringRecordAsync(CoordinationKey key, byte[] value)
    {
        var result = await Records.CreateAsync(
            key,
            value,
            RecordLifetime.ForTimeToLive(ShortTimeToLive),
            null,
            TestCancellationToken
        );

        Assert.True(result.IsWritten);
        return result.Revision;
    }

    #region CreateAsync Tests

    /// <summary>
    /// Checks that creating a record under an absent key writes it and returns the revision a read gives.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenKeyIsAbsent_WritesTheRecord()
    {
        var key = CreateKey("a");

        var revision = await CreateRecordAsync(key, "one"u8.ToArray());

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("one"u8.ToArray(), record.Value.ToArray());
        Assert.Equal(revision, record.Revision);
    }

    /// <summary>
    /// Checks that creating a record under a present key returns a conflict and keeps the stored value.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenKeyIsPresent_ReturnsConflict()
    {
        var key = CreateKey("a");
        await CreateRecordAsync(key, "one"u8.ToArray());

        var result = await Records.CreateAsync(
            key,
            "two"u8.ToArray(),
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.Equal(RecordWriteStatus.Conflict, result.Status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("one"u8.ToArray(), record.Value.ToArray());
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

        Assert.Null(await Records.ReadAsync(key, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that a record written after another record in the same keyspace has a later revision.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenAnotherKeyIsWritten_GivesALaterRevision()
    {
        var first = await CreateRecordAsync(CreateKey("a"), "one"u8.ToArray());

        var second = await CreateRecordAsync(CreateKey("b"), "two"u8.ToArray());

        Assert.True(second > first);
    }

    /// <summary>
    /// Checks that a record deleted after it was written can be created again, at a later revision.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTheRecordWasDeleted_WritesItAgain()
    {
        var key = CreateKey("a");
        var first = await CreateRecordAsync(key, "one"u8.ToArray());
        var deleted = await Records.CompareAndDeleteAsync(key, first, null, TestCancellationToken);
        Assert.Equal(RecordDeleteStatus.Deleted, deleted);

        var second = await CreateRecordAsync(key, "two"u8.ToArray());

        Assert.True(second > first);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("two"u8.ToArray(), record.Value.ToArray());
    }

    /// <summary>
    /// Checks that a record created with a time to live is present while the time to live runs, is gone once it has
    /// passed, and can then be created again.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTimeToLivePasses_ExpiresTheRecord()
    {
        var key = CreateKey("a");
        await CreateExpiringRecordAsync(key, "one"u8.ToArray());

        await AdvanceClockAsync(StillAlive);

        Assert.NotNull(await Records.ReadAsync(key, null, TestCancellationToken));

        await AdvanceClockAsync(PastExpiry);

        Assert.Null(await Records.ReadAsync(key, null, TestCancellationToken));
        await CreateRecordAsync(key, "two"u8.ToArray());
    }

    /// <summary>
    /// Checks that creating a record with a token that is already canceled throws an operation canceled exception.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Records.CreateAsync(
                CreateKey("a"),
                "one"u8.ToArray(),
                RecordLifetime.Persistent,
                null,
                new CancellationToken(canceled: true)
            )
        );
    }

    #endregion

    #region ReadAsync Tests

    /// <summary>
    /// Checks that reading an absent key returns null.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenKeyIsAbsent_ReturnsNull()
    {
        var record = await Records.ReadAsync(CreateKey("absent"), null, TestCancellationToken);

        Assert.Null(record);
    }

    /// <summary>
    /// Checks that reading with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Records.ReadAsync(CreateKey("a"), UnknownExtension, TestCancellationToken)
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
        var created = await CreateRecordAsync(key, "one"u8.ToArray());

        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            "two"u8.ToArray(),
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.True(result.IsWritten);
        Assert.True(result.Revision > created);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("two"u8.ToArray(), record.Value.ToArray());
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
        var created = await CreateRecordAsync(key, "one"u8.ToArray());
        var later = await CreateRecordAsync(CreateKey("b"), "two"u8.ToArray());
        Assert.NotEqual(created, later);

        var result = await Records.CompareAndSwapAsync(
            key,
            later,
            "three"u8.ToArray(),
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );

        Assert.Equal(RecordWriteStatus.Conflict, result.Status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("one"u8.ToArray(), record.Value.ToArray());
        Assert.Equal(created, record.Revision);
    }

    /// <summary>
    /// Checks that swapping a record under an absent key returns not found.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenKeyIsAbsent_ReturnsNotFound()
    {
        var result = await Records.CompareAndSwapAsync(
            CreateKey("absent"),
            new Revision(1),
            "one"u8.ToArray(),
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
        var created = await CreateExpiringRecordAsync(key, "one"u8.ToArray());
        await AdvanceClockAsync(TimeSpan.FromSeconds(1.5));

        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            "two"u8.ToArray(),
            RecordLifetime.ForTimeToLive(ShortTimeToLive),
            null,
            TestCancellationToken
        );
        Assert.True(result.IsWritten);

        await AdvanceClockAsync(StillAlive);

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("two"u8.ToArray(), record.Value.ToArray());

        await AdvanceClockAsync(PastExpiry);

        Assert.Null(await Records.ReadAsync(key, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that a swap with a persistent lifetime keeps the record past the time to live of the write it replaced.
    /// </summary>
    [Fact]
    public async Task CompareAndSwapAsync_WhenLifetimeIsPersistent_KeepsTheRecordPastTheEarlierTimeToLive()
    {
        var key = CreateKey("a");
        var created = await CreateExpiringRecordAsync(key, "one"u8.ToArray());

        var result = await Records.CompareAndSwapAsync(
            key,
            created,
            "two"u8.ToArray(),
            RecordLifetime.Persistent,
            null,
            TestCancellationToken
        );
        Assert.True(result.IsWritten);

        await AdvanceClockAsync(TimeSpan.FromSeconds(3));

        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("two"u8.ToArray(), record.Value.ToArray());
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
        var created = await CreateRecordAsync(key, "one"u8.ToArray());

        var status = await Records.CompareAndDeleteAsync(key, created, null, TestCancellationToken);

        Assert.Equal(RecordDeleteStatus.Deleted, status);
        Assert.Null(await Records.ReadAsync(key, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that deleting a record at a revision that is not its current one returns a conflict and keeps the
    /// record.
    /// </summary>
    [Fact]
    public async Task CompareAndDeleteAsync_WhenRevisionDiffers_ReturnsConflict()
    {
        var key = CreateKey("a");
        var created = await CreateRecordAsync(key, "one"u8.ToArray());
        var later = await CreateRecordAsync(CreateKey("b"), "two"u8.ToArray());
        Assert.NotEqual(created, later);

        var status = await Records.CompareAndDeleteAsync(key, later, null, TestCancellationToken);

        Assert.Equal(RecordDeleteStatus.Conflict, status);
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        Assert.NotNull(record);
        Assert.Equal("one"u8.ToArray(), record.Value.ToArray());
    }

    /// <summary>
    /// Checks that deleting a record under an absent key returns not found.
    /// </summary>
    [Fact]
    public async Task CompareAndDeleteAsync_WhenKeyIsAbsent_ReturnsNotFound()
    {
        var status = await Records.CompareAndDeleteAsync(
            CreateKey("absent"),
            new Revision(1),
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
        var created = await CreateRecordAsync(key, "one"u8.ToArray());

        var taken = await Records.TakeAsync(key, null, TestCancellationToken);

        Assert.NotNull(taken);
        Assert.Equal("one"u8.ToArray(), taken.Value.ToArray());
        Assert.Equal(created, taken.Revision);
        Assert.Null(await Records.ReadAsync(key, null, TestCancellationToken));
    }

    /// <summary>
    /// Checks that taking an absent key returns null.
    /// </summary>
    [Fact]
    public async Task TakeAsync_WhenKeyIsAbsent_ReturnsNull()
    {
        var taken = await Records.TakeAsync(CreateKey("absent"), null, TestCancellationToken);

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
        var keys = new[] { prefix, CreateKey("cfg", "a"), CreateKey("cfg", "b") };
        foreach (var key in keys)
        {
            await CreateRecordAsync(key, "v"u8.ToArray());
        }

        await CreateRecordAsync(CreateKey("other"), "v"u8.ToArray());

        var page = await Records.ListAsync(
            prefix,
            new RecordPageRequest(10),
            null,
            TestCancellationToken
        );

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
        var keys = new List<CoordinationKey>();
        for (var index = 0; index < count; index++)
        {
            var key = CreateKey("cfg", index);
            keys.Add(key);
            await CreateRecordAsync(key, "v"u8.ToArray());
        }

        var seen = new List<CoordinationKey>();
        string? token = null;
        var pages = 0;
        do
        {
            var page = await Records.ListAsync(
                CreateKey("cfg"),
                new RecordPageRequest(limit) { ContinuationToken = token },
                null,
                TestCancellationToken
            );

            Assert.True(page.Records.Length <= limit);
            seen.AddRange(page.Records.Select(record => record.Key));
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
        await CreateRecordAsync(CreateKey("other"), "v"u8.ToArray());

        var page = await Records.ListAsync(
            CreateKey("cfg"),
            new RecordPageRequest(10),
            null,
            TestCancellationToken
        );

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
        await CreateExpiringRecordAsync(expiring, "v"u8.ToArray());
        await CreateRecordAsync(kept, "v"u8.ToArray());

        await AdvanceClockAsync(StillAlive + PastExpiry);

        var page = await Records.ListAsync(
            CreateKey("cfg"),
            new RecordPageRequest(10),
            null,
            TestCancellationToken
        );
        var record = Assert.Single(page.Records);
        Assert.Equal(kept, record.Key);
    }

    #endregion
}
