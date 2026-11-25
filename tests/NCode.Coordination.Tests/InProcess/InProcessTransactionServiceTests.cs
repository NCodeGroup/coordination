// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessTransactionServiceTests
{
    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenTheAppliedBranchBindsToASession_ThrowsUnsupportedAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var table = new InProcessRecordTable(time);
        var records = new InProcessRecordService(table);
        var service = new InProcessTransactionService(table);
        var plainKey = InProcessTesting.Keyspace.CreateKey("plain");
        var boundKey = InProcessTesting.Keyspace.CreateKey("bound");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var sessionId = new SessionId(InProcessTesting.Keyspace, "s1");
        var session = RecordLifetime.ForSession(sessionId);
        var plainPut = RecordWrite.Put(plainKey, value, RecordLifetime.Persistent);
        var boundPut = RecordWrite.Put(boundKey, value, session);
        var transaction = new RecordTransaction([], [plainPut, boundPut]);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ExecuteAsync(transaction, null, cancellationToken)
        );

        var plain = await records.ReadAsync(plainKey, null, cancellationToken);
        var bound = await records.ReadAsync(boundKey, null, cancellationToken);

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Null(plain);
        Assert.Null(bound);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheBranchIsEmpty_ReturnsTheCurrentRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var table = new InProcessRecordTable(time);
        var records = new InProcessRecordService(table);
        var service = new InProcessTransactionService(table);
        var existingKey = InProcessTesting.Keyspace.CreateKey("existing");
        var otherKey = InProcessTesting.Keyspace.CreateKey("other");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var created = await records.CreateAsync(
            existingKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(created.IsWritten);
        var absent = RecordCondition.Absent(existingKey);
        var put = RecordWrite.Put(otherKey, value, RecordLifetime.Persistent);
        var transaction = new RecordTransaction([absent], [put]);

        var result = await service.ExecuteAsync(transaction, null, cancellationToken);

        var other = await records.ReadAsync(otherKey, null, cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(created.Revision, result.Revision);
        Assert.Null(other);
    }

    #endregion
}
