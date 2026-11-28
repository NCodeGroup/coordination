// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
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

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            )
        );
    }

    [Fact]
    public async Task CreateAsync_SessionLifetime_ReachesTheServer()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var key = keyspace.CreateKey("k");
        var lifetime = RecordLifetime.ForSession(new SessionId(keyspace, "s1"));

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

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.CompareAndSwapAsync(
                key,
                null!,
                new byte[1],
                RecordLifetime.Persistent,
                null,
                cancellationToken
            )
        );
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
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CompareAndDeleteAsync(key, new Revision(1), options, cancellationToken)
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
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

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
        var page = new RecordPageRequest(10)
        {
            ContinuationToken = PgKeys.PageToken(PgKeys.Index(other), 1),
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.ListAsync(prefix, page, null, cancellationToken)
        );

        Assert.Equal("page", exception.ParamName);
    }

    [Fact]
    public async Task ListAsync_NullPage_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var prefix = new Keyspace("prod").CreateKey("a");

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ListAsync(prefix, null!, null, cancellationToken)
        );
    }

    #endregion
}
