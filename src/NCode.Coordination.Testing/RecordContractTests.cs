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
}
