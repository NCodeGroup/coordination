// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgRecordServiceTests
{
    private static async Task WithServiceAsync(Func<PgRecordService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, settings.Schema);

        await test(new PgRecordService(new PgRecordTable(server, keyspaces, settings)));
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_ValueTooLong_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var value = new byte[CoordinationLimits.MaxValueLength + 1];
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.CreateAsync(
                    key,
                    value,
                    RecordLifetime.Persistent,
                    null,
                    cancellationToken
                )
            );
        });
    }

    [Fact]
    public async Task CreateAsync_SessionLifetime_ReachesTheServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var key = keyspace.CreateKey("k");
        var lifetime = RecordLifetime.ForSession(new SessionId(keyspace, "s1"));
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.CreateAsync(key, new byte[1], lifetime, null, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        });
    }

    #endregion

    #region CompareAndSwapAsync Tests

    [Fact]
    public async Task CompareAndSwapAsync_NullRevision_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        await WithServiceAsync(async service =>
        {
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
        });
    }

    #endregion

    #region CompareAndDeleteAsync Tests

    [Fact]
    public async Task CompareAndDeleteAsync_Extension_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.CompareAndDeleteAsync(
                    key,
                    new Revision(1),
                    options,
                    cancellationToken
                )
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_Extension_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.ReadAsync(key, options, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_TokenOfAnotherPrefix_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var prefix = keyspace.CreateKey("a");
        var other = keyspace.CreateKey("b");
        var page = new RecordPageRequest(10)
        {
            ContinuationToken = PgKeys.PageToken(PgKeys.Index(other), 1),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.ListAsync(prefix, page, null, cancellationToken)
            );

            Assert.Equal("page", exception.ParamName);
        });
    }

    [Fact]
    public async Task ListAsync_NullPage_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var prefix = new Keyspace("prod").CreateKey("a");
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await service.ListAsync(prefix, null!, null, cancellationToken)
            );
        });
    }

    #endregion
}
