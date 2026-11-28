// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgRecordServiceTests
{
    private static PgRecordService CreateService(PgOfflineServer offline)
    {
        var table = offline.Table;

        return new PgRecordService(table);
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_ValueTooLong_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var value = new byte[CoordinationLimits.MaxValueLength + 1];

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            )
        );

        Assert.Equal("value", exception.ParamName);
        Assert.StartsWith(PgCall.ValueTooLongMessage, exception.Message);
    }

    [Fact]
    public async Task CreateAsync_ValueAtTheLimit_ReachesTheServer()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var value = new byte[CoordinationLimits.MaxValueLength];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    [Fact]
    public async Task CreateAsync_SessionLifetime_ReachesTheServer()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var key = keyspace.CreateKey("k");
        var sessionId = new SessionId(keyspace, "s1");
        var lifetime = RecordLifetime.ForSession(sessionId);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CreateAsync(key, new byte[1], lifetime, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    #endregion

    #region CompareAndSwapAsync Tests

    [Fact]
    public async Task CompareAndSwapAsync_NullRevision_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.CompareAndSwapAsync(
                key,
                null!,
                new byte[1],
                RecordLifetime.Persistent,
                null,
                cancellationToken
            )
        );

        Assert.Equal("expected", exception.ParamName);
    }

    #endregion

    #region CompareAndDeleteAsync Tests

    [Fact]
    public async Task CompareAndDeleteAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var revision = new Revision(1);
        var options = new OperationOptions { Extensions = PgTestSupport.UnsupportedExtensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CompareAndDeleteAsync(key, revision, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions { Extensions = PgTestSupport.UnsupportedExtensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ReadAsync(key, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_TokenOfAnotherPrefix_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var prefix = keyspace.CreateKey("a");
        var other = keyspace.CreateKey("b");
        var otherIndex = PgKeys.Index(other);
        var token = PgKeys.PageToken(otherIndex, 1);
        var page = new RecordPageRequest(10) { ContinuationToken = token };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ListAsync(prefix, page, null, cancellationToken)
        );

        Assert.Equal("page", exception.ParamName);
        Assert.StartsWith(PgRecordService.ForeignTokenMessage, exception.Message);
    }

    [Fact]
    public async Task ListAsync_TokenOfThePrefix_ReachesTheServer()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var prefix = keyspace.CreateKey("a");
        var child = keyspace.CreateKey("a", "x");
        var childIndex = PgKeys.Index(child);
        var token = PgKeys.PageToken(childIndex, 2);
        var page = new RecordPageRequest(10) { ContinuationToken = token };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ListAsync(prefix, page, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    [Fact]
    public async Task ListAsync_NullPage_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var prefix = new Keyspace("prod").CreateKey("a");

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ListAsync(prefix, null!, null, cancellationToken)
        );

        Assert.Equal("page", exception.ParamName);
    }

    #endregion
}
