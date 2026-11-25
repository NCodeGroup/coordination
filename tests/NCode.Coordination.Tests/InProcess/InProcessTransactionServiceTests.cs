// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessTransactionServiceTests
{
    private static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Keyspace Keyspace { get; } = new("test");

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenTheAppliedBranchBindsToASession_ThrowsUnsupportedAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var table = new InProcessRecordTable(new FakeTimeProvider(Start));
        var records = new InProcessRecordService(table);
        var service = new InProcessTransactionService(table);
        var plainKey = Keyspace.CreateKey("plain");
        var boundKey = Keyspace.CreateKey("bound");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var session = RecordLifetime.ForSession(new SessionId(Keyspace, "s1"));
        var transaction = new RecordTransaction(
            [],
            [
                RecordWrite.Put(plainKey, value, RecordLifetime.Persistent),
                RecordWrite.Put(boundKey, value, session),
            ]
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ExecuteAsync(transaction, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Null(await records.ReadAsync(plainKey, null, cancellationToken));
        Assert.Null(await records.ReadAsync(boundKey, null, cancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheBranchIsEmpty_ReturnsTheCurrentRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var table = new InProcessRecordTable(new FakeTimeProvider(Start));
        var records = new InProcessRecordService(table);
        var service = new InProcessTransactionService(table);
        var existingKey = Keyspace.CreateKey("existing");
        var otherKey = Keyspace.CreateKey("other");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var created = await records.CreateAsync(
            existingKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(created.IsWritten);
        var transaction = new RecordTransaction(
            [RecordCondition.Absent(existingKey)],
            [RecordWrite.Put(otherKey, value, RecordLifetime.Persistent)]
        );

        var result = await service.ExecuteAsync(transaction, null, cancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(created.Revision, result.Revision);
        Assert.Null(await records.ReadAsync(otherKey, null, cancellationToken));
    }

    #endregion
}
