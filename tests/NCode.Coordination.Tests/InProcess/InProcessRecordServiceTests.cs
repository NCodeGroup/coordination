// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessRecordServiceTests
{
    private static InProcessRecordService CreateService()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessRecordTable(time);
        var sessions = new InProcessSessionTable(table);
        return new InProcessRecordService(sessions);
    }

    private static RecordLifetime SessionLifetime()
    {
        var sessionId = new SessionId(InProcessTesting.Keyspace, "unknown");
        return RecordLifetime.ForSession(sessionId);
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WhenTheSessionIsUnknown_ReturnsSessionEnded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var key = InProcessTesting.Keyspace.CreateKey("a");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var lifetime = SessionLifetime();

        var result = await service.CreateAsync(key, value, lifetime, null, cancellationToken);

        var stored = await service.ReadAsync(key, null, cancellationToken);

        Assert.Same(RecordWriteResult.SessionEnded, result);
        Assert.Null(stored);
    }

    [Fact]
    public async Task CreateAsync_WhenFirstInItsKeyspace_ReturnsRevisionTwo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var key = InProcessTesting.Keyspace.CreateKey("a");
        ReadOnlyMemory<byte> value = (byte[])[1];

        var result = await service.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        var expected = new Revision(2);

        Assert.True(result.IsWritten);
        Assert.Equal(expected, result.Revision);
    }

    #endregion

    #region CompareAndSwapAsync Tests

    [Fact]
    public async Task CompareAndSwapAsync_WhenTheSessionIsUnknown_ReturnsSessionEnded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var key = InProcessTesting.Keyspace.CreateKey("a");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var created = await service.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(created.IsWritten);
        var lifetime = SessionLifetime();

        var result = await service.CompareAndSwapAsync(
            key,
            created.Revision,
            (byte[])[2],
            lifetime,
            null,
            cancellationToken
        );

        Assert.Same(RecordWriteResult.SessionEnded, result);
        var stored = await service.ReadAsync(key, null, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(created.Revision, stored.Revision);
        var isSameValue = value.Span.SequenceEqual(stored.Value.Span);
        Assert.True(isSameValue);
    }

    #endregion

    #region ListAsync Tests

    [Theory]
    [InlineData("x")]
    [InlineData("0")]
    public async Task ListAsync_WhenTheTokenWasNotIssued_ThrowsArgumentException(string token)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var prefix = InProcessTesting.Keyspace.CreateKey("a");
        var page = new RecordPageRequest(10) { ContinuationToken = token };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ListAsync(prefix, page, null, cancellationToken)
        );

        Assert.Equal("page", exception.ParamName);
    }

    [Fact]
    public async Task ListAsync_WhenKeyspaceWasNeverWritten_ReturnsRevisionOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var prefix = InProcessTesting.Keyspace.CreateKey("a");
        var page = new RecordPageRequest(10);

        var result = await service.ListAsync(prefix, page, null, cancellationToken);

        var expected = new Revision(1);

        Assert.Equal(expected, result.Revision);
        Assert.Empty(result.Records);
    }

    [Fact]
    public async Task ListAsync_WhenAnotherKeyspaceHasRecordsUnderTheSameSegments_ListsOnlyItsOwnKeyspace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var ownKey = InProcessTesting.Keyspace.CreateKey("cfg", "a");
        var otherKeyspace = new Keyspace("other");
        var otherKey = otherKeyspace.CreateKey("cfg", "a");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var ownCreated = await service.CreateAsync(
            ownKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(ownCreated.IsWritten);
        var otherCreated = await service.CreateAsync(
            otherKey,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(otherCreated.IsWritten);
        var prefix = InProcessTesting.Keyspace.CreateKey("cfg");
        var page = new RecordPageRequest(10);

        var result = await service.ListAsync(prefix, page, null, cancellationToken);

        var record = Assert.Single(result.Records);
        Assert.Equal(ownKey, record.Key);
    }

    #endregion
}
