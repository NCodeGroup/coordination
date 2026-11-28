// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using NCode.Coordination.PostgreSql.Subscriptions;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgRecordWatchServiceTests
{
    private static PgRecordWatchService CreateService(PgOfflineServer offline)
    {
        var changeLog = new PgChangeLog(offline.Server, offline.Settings.Schema);
        var hub = new PgLogHub(
            offline.Source,
            offline.Keyspaces,
            offline.Settings,
            TimeProvider.System,
            NullLoggerFactory.Instance
        );

        return new PgRecordWatchService(offline.Keyspaces, changeLog, hub);
    }

    #region WatchKeyAsync Tests

    [Fact]
    public async Task WatchKeyAsync_NullKey_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in service.WatchKeyAsync(null!, null, null, cancellationToken)) { }
        });
    }

    #endregion

    #region WatchPrefixAsync Tests

    [Fact]
    public async Task WatchPrefixAsync_Extension_ThrowsUnsupportedOnFirstMove()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var prefix = new Keyspace("prod").CreateKey("p");
        var extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s");
        var options = new OperationOptions { Extensions = extensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
        {
            await foreach (
                var _ in service.WatchPrefixAsync(prefix, null, options, cancellationToken)
            ) { }
        });

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
