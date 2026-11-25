// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the transaction primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class TransactionContractTests : ProviderContractTests
{
    /// <summary>
    /// Gets the provider's transaction service, and skips the test when the provider does not support transactions.
    /// </summary>
    private ITransactionService Transactions =>
        GetService<ITransactionService>(Primitives.Transaction);

    /// <summary>
    /// Gets the provider's record service, which the tests use to seed and read records, and skips the test when the
    /// provider does not support records.
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
    /// Creates a persistent put of a value.
    /// </summary>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The write.</returns>
    private static RecordWrite Put(CoordinationKey key, byte[] value) =>
        RecordWrite.Put(key, value, RecordLifetime.Persistent);

    /// <summary>
    /// Reads the value of a record, or null when the key is absent.
    /// </summary>
    /// <param name="key">The key to read.</param>
    /// <returns>The value, or null.</returns>
    private async ValueTask<byte[]?> ReadValueAsync(CoordinationKey key)
    {
        var record = await Records.ReadAsync(key, null, TestCancellationToken);
        return record?.Value.ToArray();
    }

    #region ExecuteAsync Tests

    /// <summary>
    /// Checks that a transaction whose conditions all hold applies its success writes at the revision it returns and
    /// leaves its failure writes out.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenEveryConditionHolds_AppliesTheSuccessWrites()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var key3 = CreateKey("k3");
        var revision1 = await CreateRecordAsync(key1, "one"u8.ToArray());
        var transaction = new RecordTransaction(
            [RecordCondition.RevisionEquals(key1, revision1), RecordCondition.Absent(key2)],
            [Put(key1, "uno"u8.ToArray()), Put(key2, "dos"u8.ToArray())]
        )
        {
            OnFailure = [Put(key3, "tres"u8.ToArray())],
        };

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        var record1 = await Records.ReadAsync(key1, null, TestCancellationToken);
        Assert.NotNull(record1);
        Assert.Equal("uno"u8.ToArray(), record1.Value.ToArray());
        Assert.Equal(result.Revision, record1.Revision);
        var record2 = await Records.ReadAsync(key2, null, TestCancellationToken);
        Assert.NotNull(record2);
        Assert.Equal("dos"u8.ToArray(), record2.Value.ToArray());
        Assert.Equal(result.Revision, record2.Revision);
        Assert.Null(await ReadValueAsync(key3));
    }

    /// <summary>
    /// Checks that a transaction with a failing condition applies its failure writes and leaves its success writes
    /// out.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAConditionFails_AppliesTheFailureWrites()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var key3 = CreateKey("k3");
        await CreateRecordAsync(key1, "one"u8.ToArray());
        var transaction = new RecordTransaction(
            [RecordCondition.Absent(key1)],
            [Put(key2, "dos"u8.ToArray())]
        )
        {
            OnFailure = [RecordWrite.Delete(key1), Put(key3, "tres"u8.ToArray())],
        };

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(await ReadValueAsync(key1));
        Assert.Equal("tres"u8.ToArray(), await ReadValueAsync(key3));
        Assert.Null(await ReadValueAsync(key2));
    }

    /// <summary>
    /// Checks that a value condition holds when the stored value equals the given value.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenValueEqualsHolds_Succeeds()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        await CreateRecordAsync(key1, "one"u8.ToArray());
        var transaction = new RecordTransaction(
            [RecordCondition.ValueEquals(key1, "one"u8.ToArray())],
            [Put(key2, "dos"u8.ToArray())]
        );

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("dos"u8.ToArray(), await ReadValueAsync(key2));
    }

    /// <summary>
    /// Checks that a value condition fails when the stored value differs from the given value.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenValueEqualsDiffers_Fails()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        await CreateRecordAsync(key1, "one"u8.ToArray());
        var transaction = new RecordTransaction(
            [RecordCondition.ValueEquals(key1, "other"u8.ToArray())],
            [Put(key2, "dos"u8.ToArray())]
        );

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(await ReadValueAsync(key2));
    }

    /// <summary>
    /// Checks that a transaction without conditions succeeds and applies its success writes.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenNoConditionIsGiven_Succeeds()
    {
        var key = CreateKey("k1");
        var transaction = new RecordTransaction([], [Put(key, "one"u8.ToArray())]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("one"u8.ToArray(), await ReadValueAsync(key));
    }

    /// <summary>
    /// Checks that a transaction with a failing condition and no failure writes leaves the records as they were.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAConditionFailsWithoutFailureWrites_ChangesNothing()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        await CreateRecordAsync(key1, "one"u8.ToArray());
        var transaction = new RecordTransaction(
            [RecordCondition.Absent(key1)],
            [Put(key2, "dos"u8.ToArray())]
        );

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("one"u8.ToArray(), await ReadValueAsync(key1));
        Assert.Null(await ReadValueAsync(key2));
    }

    /// <summary>
    /// Checks that a transaction that writes returns a revision later than the revision of an earlier write.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenItWrites_GivesALaterRevision()
    {
        var earlier = await CreateRecordAsync(CreateKey("k1"), "one"u8.ToArray());
        var transaction = new RecordTransaction([], [Put(CreateKey("k2"), "dos"u8.ToArray())]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Revision > earlier);
    }

    /// <summary>
    /// Checks that a record put with a time to live expires when the time passes.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAPutCarriesATimeToLive_ExpiresTheRecord()
    {
        var key = CreateKey("k1");
        var transaction = new RecordTransaction(
            [],
            [RecordWrite.Put(key, "one"u8.ToArray(), RecordLifetime.ForTimeToLive(ShortTimeToLive))]
        );
        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);
        Assert.True(result.Succeeded);

        await AdvanceClockAsync(StillAlive);
        Assert.Equal("one"u8.ToArray(), await ReadValueAsync(key));

        await AdvanceClockAsync(PastExpiry);
        Assert.Null(await ReadValueAsync(key));
    }

    /// <summary>
    /// Checks that an absence condition holds for a record whose time to live has ended.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAbsentMeetsAnExpiredRecord_Holds()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var created = await Records.CreateAsync(
            key1,
            "one"u8.ToArray(),
            RecordLifetime.ForTimeToLive(ShortTimeToLive),
            null,
            TestCancellationToken
        );
        Assert.True(created.IsWritten);
        await AdvanceClockAsync(StillAlive);
        await AdvanceClockAsync(PastExpiry);
        var transaction = new RecordTransaction(
            [RecordCondition.Absent(key1)],
            [Put(key2, "dos"u8.ToArray())]
        );

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("dos"u8.ToArray(), await ReadValueAsync(key2));
    }

    /// <summary>
    /// Checks that a provider that declares atomicity across partitions applies the writes of keys in different
    /// partitions together.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenKeysSpanPartitionsAndAtomicityAcrossPartitionsIsDeclared_AppliesEveryWrite()
    {
        Assert.SkipUnless(
            Declares(Primitives.Transaction, Guarantees.AtomicAcrossPartitions),
            "The provider does not declare atomicity across partitions."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };
        var transaction = new RecordTransaction(
            [RecordCondition.Absent(key1), RecordCondition.Absent(key2)],
            [Put(key1, "one"u8.ToArray()), Put(key2, "two"u8.ToArray())]
        );

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("one"u8.ToArray(), await ReadValueAsync(key1));
        Assert.Equal("two"u8.ToArray(), await ReadValueAsync(key2));
    }

    /// <summary>
    /// Checks that a provider that declares only atomicity within a partition refuses a transaction whose keys are in
    /// different partitions.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenKeysSpanPartitionsAndOnlyAtomicityWithinPartitionIsDeclared_ThrowsUnsupported()
    {
        Assert.SkipUnless(
            Declares(Primitives.Transaction, Guarantees.AtomicWithinPartition)
                && !Declares(Primitives.Transaction, Guarantees.AtomicAcrossPartitions),
            "The provider does not declare atomicity within a partition only."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };
        var transaction = new RecordTransaction(
            [],
            [Put(key1, "one"u8.ToArray()), Put(key2, "two"u8.ToArray())]
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Transactions.ExecuteAsync(transaction, null, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    /// <summary>
    /// Checks that executing with an option extension that the provider does not know throws an unsupported error.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenOptionsCarryAnUnknownExtension_ThrowsUnsupported()
    {
        var transaction = new RecordTransaction([], [Put(CreateKey("k1"), "one"u8.ToArray())]);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await Transactions.ExecuteAsync(transaction, UnknownExtension, TestCancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    /// <summary>
    /// Checks that executing with a canceled token throws an operation canceled error.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        var transaction = new RecordTransaction([], [Put(CreateKey("k1"), "one"u8.ToArray())]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Transactions.ExecuteAsync(
                transaction,
                null,
                new CancellationToken(canceled: true)
            )
        );
    }

    #endregion
}
