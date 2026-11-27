// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgRecordWatchServiceTests
{
    private static async Task WithServiceAsync(Func<PgRecordWatchService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, PgSchemaName.Default);
        await using var hub = new PgLogHub(source, keyspaces, settings, TimeProvider.System);

        await test(
            new PgRecordWatchService(keyspaces, new PgChangeLog(server, PgSchemaName.Default), hub)
        );
    }

    #region WatchKeyAsync Tests

    [Fact]
    public async Task WatchKeyAsync_NullKey_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await foreach (var _ in service.WatchKeyAsync(null!, null, null, cancellationToken))
                { }
            });
        });
    }

    #endregion

    #region WatchPrefixAsync Tests

    [Fact]
    public async Task WatchPrefixAsync_Extension_ThrowsUnsupportedOnFirstMove()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var prefix = new Keyspace("prod").CreateKey("p");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            {
                await foreach (
                    var _ in service.WatchPrefixAsync(prefix, null, options, cancellationToken)
                ) { }
            });

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion
}
