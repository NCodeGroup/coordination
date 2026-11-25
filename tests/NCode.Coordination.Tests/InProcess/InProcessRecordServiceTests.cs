// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessRecordServiceTests
{
    private static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Keyspace Keyspace { get; } = new("test");

    private static InProcessRecordService CreateService() =>
        new(new InProcessRecordTable(new FakeTimeProvider(Start)));

    private static RecordLifetime SessionLifetime() =>
        RecordLifetime.ForSession(new SessionId(Keyspace, "s1"));

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WhenLifetimeIsBoundToASession_ReturnsSessionEnded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var key = Keyspace.CreateKey("a");
        ReadOnlyMemory<byte> value = (byte[])[1];

        var result = await service.CreateAsync(
            key,
            value,
            SessionLifetime(),
            null,
            cancellationToken
        );

        Assert.Same(RecordWriteResult.SessionEnded, result);
        Assert.Null(await service.ReadAsync(key, null, cancellationToken));
    }

    [Fact]
    public async Task CreateAsync_WhenFirstInItsKeyspace_ReturnsRevisionTwo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var key = Keyspace.CreateKey("a");
        ReadOnlyMemory<byte> value = (byte[])[1];

        var result = await service.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );

        Assert.True(result.IsWritten);
        Assert.Equal(new Revision(2), result.Revision);
    }

    #endregion

    #region CompareAndSwapAsync Tests

    [Fact]
    public async Task CompareAndSwapAsync_WhenLifetimeIsBoundToASession_ReturnsSessionEnded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService();
        var key = Keyspace.CreateKey("a");
        ReadOnlyMemory<byte> value = (byte[])[1];
        var created = await service.CreateAsync(
            key,
            value,
            RecordLifetime.Persistent,
            null,
            cancellationToken
        );
        Assert.True(created.IsWritten);

        var result = await service.CompareAndSwapAsync(
            key,
            created.Revision,
            (byte[])[2],
            SessionLifetime(),
            null,
            cancellationToken
        );

        Assert.Same(RecordWriteResult.SessionEnded, result);
        var stored = await service.ReadAsync(key, null, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(created.Revision, stored.Revision);
        Assert.True(value.Span.SequenceEqual(stored.Value.Span));
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
        var prefix = Keyspace.CreateKey("a");
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
        var prefix = Keyspace.CreateKey("a");

        var result = await service.ListAsync(
            prefix,
            new RecordPageRequest(10),
            null,
            cancellationToken
        );

        Assert.Equal(new Revision(1), result.Revision);
        Assert.Empty(result.Records);
    }

    #endregion
}
