// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Testing.Checking;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using NCode.Coordination.Testing.Runs;
using Xunit;

namespace NCode.Coordination.Testing;

/// <summary>
/// The contract suite of the transaction primitive: a provider's test project derives a sealed class from it and
/// overrides <see cref="ProviderContractTests.CreateTargetAsync"/>.
/// </summary>
public abstract class TransactionContractTests : ProviderContractTests
{
    /// <summary>
    /// Creates a persistent put of a value.
    /// </summary>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The write.</returns>
    private static RecordWrite Put(CoordinationKey key, byte[] value) =>
        RecordWrite.Put(key, value, RecordLifetime.Persistent);

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
    /// Gets the provider's session service, and skips the test when the provider does not support sessions.
    /// </summary>
    private ISessionService Sessions => GetService<ISessionService>(Primitives.Session);

    /// <summary>
    /// Reads the value of a record, or null when the key is absent.
    /// </summary>
    /// <param name="key">The key to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The value, or null.</returns>
    private async ValueTask<byte[]?> ReadValueAsync(
        CoordinationKey key,
        CancellationToken cancellationToken
    )
    {
        var record = await Records.ReadAsync(key, null, cancellationToken);
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
        var one = "one"u8.ToArray();
        var uno = "uno"u8.ToArray();
        var dos = "dos"u8.ToArray();
        var tres = "tres"u8.ToArray();
        var revision1 = await ContractRecords.CreateAsync(
            Records,
            key1,
            one,
            TestCancellationToken
        );
        var revisionEquals = RecordCondition.RevisionEquals(key1, revision1);
        var absent = RecordCondition.Absent(key2);
        var putUno = Put(key1, uno);
        var putDos = Put(key2, dos);
        var putTres = Put(key3, tres);
        var transaction = new RecordTransaction([revisionEquals, absent], [putUno, putDos])
        {
            OnFailure = [putTres],
        };

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        var record1 = await Records.ReadAsync(key1, null, TestCancellationToken);
        Assert.NotNull(record1);
        var actual1 = record1.Value.ToArray();
        Assert.Equal(uno, actual1);
        Assert.Equal(result.Revision, record1.Revision);
        var record2 = await Records.ReadAsync(key2, null, TestCancellationToken);
        Assert.NotNull(record2);
        var actual2 = record2.Value.ToArray();
        Assert.Equal(dos, actual2);
        Assert.Equal(result.Revision, record2.Revision);
        var actual3 = await ReadValueAsync(key3, TestCancellationToken);
        Assert.Null(actual3);
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
        var one = "one"u8.ToArray();
        var dos = "dos"u8.ToArray();
        var tres = "tres"u8.ToArray();
        await ContractRecords.CreateAsync(Records, key1, one, TestCancellationToken);
        var absent = RecordCondition.Absent(key1);
        var putDos = Put(key2, dos);
        var deleteKey1 = RecordWrite.Delete(key1);
        var putTres = Put(key3, tres);
        var transaction = new RecordTransaction([absent], [putDos])
        {
            OnFailure = [deleteKey1, putTres],
        };

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.False(result.Succeeded);
        var actual1 = await ReadValueAsync(key1, TestCancellationToken);
        Assert.Null(actual1);
        var actual3 = await ReadValueAsync(key3, TestCancellationToken);
        Assert.Equal(tres, actual3);
        var actual2 = await ReadValueAsync(key2, TestCancellationToken);
        Assert.Null(actual2);
    }

    /// <summary>
    /// Checks that a value condition holds when the stored value equals the given value.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenValueEqualsHolds_Succeeds()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var one = "one"u8.ToArray();
        var dos = "dos"u8.ToArray();
        await ContractRecords.CreateAsync(Records, key1, one, TestCancellationToken);
        var valueEquals = RecordCondition.ValueEquals(key1, one);
        var putDos = Put(key2, dos);
        var transaction = new RecordTransaction([valueEquals], [putDos]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        var actual = await ReadValueAsync(key2, TestCancellationToken);
        Assert.Equal(dos, actual);
    }

    /// <summary>
    /// Checks that a value condition fails when the stored value differs from the given value.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenValueEqualsDiffers_Fails()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var one = "one"u8.ToArray();
        var other = "other"u8.ToArray();
        var dos = "dos"u8.ToArray();
        await ContractRecords.CreateAsync(Records, key1, one, TestCancellationToken);
        var valueEquals = RecordCondition.ValueEquals(key1, other);
        var putDos = Put(key2, dos);
        var transaction = new RecordTransaction([valueEquals], [putDos]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.False(result.Succeeded);
        var actual = await ReadValueAsync(key2, TestCancellationToken);
        Assert.Null(actual);
    }

    /// <summary>
    /// Checks that a transaction without conditions succeeds and applies its success writes.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenNoConditionIsGiven_Succeeds()
    {
        var key = CreateKey("k1");
        var one = "one"u8.ToArray();
        var putOne = Put(key, one);
        var transaction = new RecordTransaction([], [putOne]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        var actual = await ReadValueAsync(key, TestCancellationToken);
        Assert.Equal(one, actual);
    }

    /// <summary>
    /// Checks that a transaction with a failing condition and no failure writes leaves the records as they were.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAConditionFailsWithoutFailureWrites_ChangesNothing()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var one = "one"u8.ToArray();
        var dos = "dos"u8.ToArray();
        await ContractRecords.CreateAsync(Records, key1, one, TestCancellationToken);
        var absent = RecordCondition.Absent(key1);
        var putDos = Put(key2, dos);
        var transaction = new RecordTransaction([absent], [putDos]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.False(result.Succeeded);
        var actual1 = await ReadValueAsync(key1, TestCancellationToken);
        Assert.Equal(one, actual1);
        var actual2 = await ReadValueAsync(key2, TestCancellationToken);
        Assert.Null(actual2);
    }

    /// <summary>
    /// Checks that a transaction that writes returns a revision later than the revision of an earlier write.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenItWrites_GivesALaterRevision()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var one = "one"u8.ToArray();
        var dos = "dos"u8.ToArray();
        var earlier = await ContractRecords.CreateAsync(Records, key1, one, TestCancellationToken);
        var putDos = Put(key2, dos);
        var transaction = new RecordTransaction([], [putDos]);

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
        var one = "one"u8.ToArray();
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        var putOne = RecordWrite.Put(key, one, lifetime);
        var transaction = new RecordTransaction([], [putOne]);
        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);
        Assert.True(result.Succeeded);

        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        var alive = await ReadValueAsync(key, TestCancellationToken);
        Assert.Equal(one, alive);

        await AdvanceClockAsync(PastExpiry, TestCancellationToken);
        var expired = await ReadValueAsync(key, TestCancellationToken);
        Assert.Null(expired);
    }

    /// <summary>
    /// Checks that a record put bound to a live session is written and is deleted when the session closes.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAPutIsBoundToALiveSession_EndsTheRecordWithTheSession()
    {
        var key = CreateKey("k1");
        var one = "one"u8.ToArray();
        var session = await ContractSessions.OpenAsync(
            Sessions,
            Keyspace,
            LongTimeToLive,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);
        var putOne = RecordWrite.Put(key, one, lifetime);
        var transaction = new RecordTransaction([], [putOne]);
        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);
        Assert.True(result.Succeeded);
        var alive = await ReadValueAsync(key, TestCancellationToken);
        Assert.Equal(one, alive);

        await Sessions.CloseAsync(session, null, TestCancellationToken);

        var ended = await ReadValueAsync(key, TestCancellationToken);
        Assert.Null(ended);
    }

    /// <summary>
    /// Checks that a transaction whose applied writes bind a record to a session that has ended writes nothing and
    /// reports that the session ended.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheAppliedBranchBindsToAnEndedSession_ReturnsSessionEnded()
    {
        var plainKey = CreateKey("k1");
        var boundKey = CreateKey("k2");
        var one = "one"u8.ToArray();
        var session = await ContractSessions.OpenEndedAsync(
            Sessions,
            Keyspace,
            TestCancellationToken
        );
        var lifetime = RecordLifetime.ForSession(session);
        var putPlain = Put(plainKey, one);
        var putBound = RecordWrite.Put(boundKey, one, lifetime);
        var transaction = new RecordTransaction([], [putPlain, putBound]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.Equal(TransactionStatus.SessionEnded, result.Status);
        var plain = await ReadValueAsync(plainKey, TestCancellationToken);
        var bound = await ReadValueAsync(boundKey, TestCancellationToken);
        Assert.Null(plain);
        Assert.Null(bound);
    }

    /// <summary>
    /// Checks that an absence condition holds for a record whose time to live has ended.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenAbsentMeetsAnExpiredRecord_Holds()
    {
        var key1 = CreateKey("k1");
        var key2 = CreateKey("k2");
        var one = "one"u8.ToArray();
        var dos = "dos"u8.ToArray();
        var lifetime = RecordLifetime.ForTimeToLive(ShortTimeToLive);
        var created = await Records.CreateAsync(key1, one, lifetime, null, TestCancellationToken);
        Assert.True(created.IsWritten);
        await AdvanceClockAsync(StillAlive, TestCancellationToken);
        await AdvanceClockAsync(PastExpiry, TestCancellationToken);
        var absent = RecordCondition.Absent(key1);
        var putDos = Put(key2, dos);
        var transaction = new RecordTransaction([absent], [putDos]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        var actual = await ReadValueAsync(key2, TestCancellationToken);
        Assert.Equal(dos, actual);
    }

    /// <summary>
    /// Checks that a provider that declares atomicity across partitions applies the writes of keys in different
    /// partitions together.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenKeysSpanPartitionsAndAtomicityAcrossPartitionsIsDeclared_AppliesEveryWrite()
    {
        var declaresAcross = Declares(Primitives.Transaction, Guarantees.AtomicAcrossPartitions);
        Assert.SkipUnless(
            declaresAcross,
            "The provider does not declare atomicity across partitions."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var absent1 = RecordCondition.Absent(key1);
        var absent2 = RecordCondition.Absent(key2);
        var putOne = Put(key1, one);
        var putTwo = Put(key2, two);
        var transaction = new RecordTransaction([absent1, absent2], [putOne, putTwo]);

        var result = await Transactions.ExecuteAsync(transaction, null, TestCancellationToken);

        Assert.True(result.Succeeded);
        var actual1 = await ReadValueAsync(key1, TestCancellationToken);
        Assert.Equal(one, actual1);
        var actual2 = await ReadValueAsync(key2, TestCancellationToken);
        Assert.Equal(two, actual2);
    }

    /// <summary>
    /// Checks that a provider that declares only atomicity within a partition refuses a transaction whose keys are in
    /// different partitions.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenKeysSpanPartitionsAndOnlyAtomicityWithinPartitionIsDeclared_ThrowsUnsupported()
    {
        var declaresWithin = Declares(Primitives.Transaction, Guarantees.AtomicWithinPartition);
        var declaresAcross = Declares(Primitives.Transaction, Guarantees.AtomicAcrossPartitions);
        Assert.SkipUnless(
            declaresWithin && !declaresAcross,
            "The provider does not declare atomicity within a partition only."
        );
        var key1 = CreateKey("p1", "a") with { PartitionDepth = 1 };
        var key2 = CreateKey("p2", "b") with { PartitionDepth = 1 };
        var one = "one"u8.ToArray();
        var two = "two"u8.ToArray();
        var putOne = Put(key1, one);
        var putTwo = Put(key2, two);
        var transaction = new RecordTransaction([], [putOne, putTwo]);

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
        var key = CreateKey("k1");
        var one = "one"u8.ToArray();
        var putOne = Put(key, one);
        var transaction = new RecordTransaction([], [putOne]);

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
        var key = CreateKey("k1");
        var one = "one"u8.ToArray();
        var putOne = Put(key, one);
        var transaction = new RecordTransaction([], [putOne]);
        var canceledToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Transactions.ExecuteAsync(transaction, null, canceledToken)
        );
    }

    #endregion

    #region Concurrent Run Tests

    /// <summary>
    /// Checks that the history of clients that read two records and write them together with transactions at once is
    /// linearizable.
    /// </summary>
    [Fact]
    public async Task ConcurrentRun_WhenClientsRace_KeepsALinearizableHistory()
    {
        SkipUnlessLinearizable(Primitives.Transaction);
        var transactions = Transactions;
        var records = Records;
        var first = CreateKey("tx", "a") with { PartitionDepth = 1 };
        var second = CreateKey("tx", "b") with { PartitionDepth = 1 };
        var lastSeen = new string?[ConcurrentRun.Clients];
        var recorder = new HistoryRecorder();
        const string partition = "transaction";

        await ConcurrentRun.RunAsync(
            async (client, index, cancellationToken) =>
            {
                switch (index % 3)
                {
                    case 0:
                        var readFirst = new TransactionCall(
                            TransactionCall.Read,
                            TransactionCall.FirstKey,
                            null,
                            null
                        );
                        var observed = await recorder.RecordAsync(
                            client,
                            partition,
                            readFirst,
                            token => records.ReadAsync(first, null, token),
                            cancellationToken
                        );
                        if (observed.Outcome == HistoryOutcome.Returned)
                        {
                            lastSeen[client] = observed.Output is StoredRecord stored
                                ? HistoryValues.Decode(stored.Value)
                                : null;
                        }

                        break;

                    case 1:
                        var readSecond = new TransactionCall(
                            TransactionCall.Read,
                            TransactionCall.SecondKey,
                            null,
                            null
                        );
                        await recorder.RecordAsync(
                            client,
                            partition,
                            readSecond,
                            token => records.ReadAsync(second, null, token),
                            cancellationToken
                        );
                        break;

                    default:
                        var expected = lastSeen[client];
                        var value = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{client}-{index}"
                        );
                        var bytes = HistoryValues.Encode(value);
                        var condition = expected is null
                            ? RecordCondition.Absent(first)
                            : RecordCondition.ValueEquals(first, HistoryValues.Encode(expected));
                        var transaction = new RecordTransaction(
                            [condition],
                            [Put(first, bytes), Put(second, bytes)]
                        );
                        var execute = new TransactionCall(
                            TransactionCall.Execute,
                            null,
                            expected,
                            value
                        );
                        await recorder.RecordAsync(
                            client,
                            partition,
                            execute,
                            token => transactions.ExecuteAsync(transaction, null, token),
                            cancellationToken
                        );
                        break;
                }
            },
            TestCancellationToken
        );

        HistoryAssert.Linearizable(new TransactionModel(), recorder.ToHistory());
    }

    #endregion
}
