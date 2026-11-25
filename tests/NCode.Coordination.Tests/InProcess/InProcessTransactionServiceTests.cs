// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessTransactionServiceTests
{
    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenTheAppliedBranchBindsToAnEndedSession_ReturnsSessionEnded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var table = new InProcessRecordTable(time);
        var sessions = new InProcessSessionTable(table);
        var service = new InProcessTransactionService(sessions);
        var plainKey = InProcessTesting.Keyspace.CreateKey("plain");
        var boundKey = InProcessTesting.Keyspace.CreateKey("bound");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var sessionId = new SessionId(InProcessTesting.Keyspace, "unknown");
        var session = RecordLifetime.ForSession(sessionId);
        var plainPut = RecordWrite.Put(plainKey, value, RecordLifetime.Persistent);
        var boundPut = RecordWrite.Put(boundKey, value, session);
        var transaction = new RecordTransaction([], [plainPut, boundPut]);

        var result = await service.ExecuteAsync(transaction, null, cancellationToken);

        Assert.Equal(TransactionStatus.SessionEnded, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheAppliedBranchBindsToAnEndedSession_WritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var table = new InProcessRecordTable(time);
        var sessions = new InProcessSessionTable(table);
        var records = new InProcessRecordService(sessions);
        var service = new InProcessTransactionService(sessions);
        var plainKey = InProcessTesting.Keyspace.CreateKey("plain");
        var boundKey = InProcessTesting.Keyspace.CreateKey("bound");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var sessionId = new SessionId(InProcessTesting.Keyspace, "unknown");
        var session = RecordLifetime.ForSession(sessionId);
        var plainPut = RecordWrite.Put(plainKey, value, RecordLifetime.Persistent);
        var boundPut = RecordWrite.Put(boundKey, value, session);
        var transaction = new RecordTransaction([], [plainPut, boundPut]);

        await service.ExecuteAsync(transaction, null, cancellationToken);

        var plain = await records.ReadAsync(plainKey, null, cancellationToken);
        var bound = await records.ReadAsync(boundKey, null, cancellationToken);

        Assert.Null(plain);
        Assert.Null(bound);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPutIsBoundToALiveSession_EndsTheRecordWithTheSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var table = new InProcessRecordTable(time);
        var sessions = new InProcessSessionTable(table);
        var records = new InProcessRecordService(sessions);
        var service = new InProcessTransactionService(sessions);
        var sessionService = new InProcessSessionService(sessions);
        var boundKey = InProcessTesting.Keyspace.CreateKey("bound");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var grant = await sessionService.OpenAsync(
            InProcessTesting.Keyspace,
            new TimeToLive(TimeSpan.FromSeconds(30)),
            null,
            cancellationToken
        );
        var boundPut = RecordWrite.Put(boundKey, value, RecordLifetime.ForSession(grant.Id));
        var transaction = new RecordTransaction([], [boundPut]);

        var result = await service.ExecuteAsync(transaction, null, cancellationToken);

        var whileLive = await records.ReadAsync(boundKey, null, cancellationToken);

        await sessionService.CloseAsync(grant.Id, null, cancellationToken);

        var afterClose = await records.ReadAsync(boundKey, null, cancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(whileLive);
        Assert.Null(afterClose);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAConditionFailsWithoutFailureWrites_ReturnsTheCurrentRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = InProcessTesting.CreateTime();
        var table = new InProcessRecordTable(time);
        var sessions = new InProcessSessionTable(table);
        var records = new InProcessRecordService(sessions);
        var service = new InProcessTransactionService(sessions);
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
