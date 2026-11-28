// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Logging;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Subscriptions.Tests;

public sealed class PgLogHubServerTests(PgServers servers)
{
    private static TimeSpan Patience { get; } = TimeSpan.FromSeconds(15);

    private sealed record TestEntry(long Number) : IPgLogEntry;

    private async Task<IReadOnlyList<FakeLogRecord>> DeliverAfterFailedReadAsync(
        Exception failure,
        CancellationToken cancellationToken
    )
    {
        var schemaName = "hub_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var schema = PgSchemaName.Create(schemaName);
        var checkInterval = TimeSpan.FromSeconds(1);
        var settings = PgSettings.Default with { Schema = schema, CheckInterval = checkInterval };
        var options = new FakeLogCollectorOptions();
        var collector = FakeLogCollector.Create(options);
        var loggerFactory = new PgCollectingLoggerFactory(collector);
        var calls = 0;
        var id = new PgLogId(1, null);

        ValueTask<PgLogRead<TestEntry>> Read(long after, int count, CancellationToken token)
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                var empty = new PgLogRead<TestEntry>(0, 0, []);
                return ValueTask.FromResult(empty);
            }

            if (call == 2)
            {
                return ValueTask.FromException<PgLogRead<TestEntry>>(failure);
            }

            var entry = new TestEntry(1);
            var read = new PgLogRead<TestEntry>(0, 1, [entry]);
            return ValueTask.FromResult(read);
        }

        try
        {
            await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
            var server = new PgServer(source, settings);
            var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, settings);
            await probe.RunAsync(cancellationToken);

            var tokens = new PgRandomTokens();
            var keyspaces = new PgKeyspaces(server, schema, tokens);
            await using var hub = new PgLogHub(
                source,
                keyspaces,
                settings,
                TimeProvider.System,
                loggerFactory
            );

            var subscriber = await hub.SubscribeAsync(
                id,
                () => new PgLogFanout<TestEntry>(Read),
                0,
                cancellationToken
            );

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Patience);
            var delivered = await subscriber.Reader.ReadAsync(timeout.Token);

            Assert.False(delivered.IsResync);
            Assert.Equal(1, delivered.Current);
            var expectedEntry = new TestEntry(1);
            Assert.Equal(expectedEntry, delivered.Entry);
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                servers.Admin19,
                $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE",
                CancellationToken.None
            );
        }

        return collector.GetSnapshot();
    }

    #region CheckAsync Tests

    [PgServerFact]
    public async Task CheckAsync_PumpThrowsUnexpectedly_NextCheckStillDelivers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("The read failed unexpectedly.");

        var records = await DeliverAfterFailedReadAsync(failure, cancellationToken);

        var record = Assert.Single(records, x => x.Id.Id == EventIds.PumpFaulted);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Same(failure, record.Exception);
    }

    [PgServerFact]
    public async Task CheckAsync_PumpFailsWithACoordinationFailure_LogsPumpFailedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new CoordinationException(CoordinationErrorCodes.Unavailable, false, "down");

        var records = await DeliverAfterFailedReadAsync(failure, cancellationToken);

        var record = Assert.Single(records, x => x.Id.Id == EventIds.PumpFailed);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Same(failure, record.Exception);
    }

    [PgServerFact]
    public async Task CheckAsync_PumpFindsTheKeyspaceMissing_LogsPumpFailedOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new PgKeyspaceMissingException(PgKeyspaceMissingException.RowMissingMessage);

        var records = await DeliverAfterFailedReadAsync(failure, cancellationToken);

        var record = Assert.Single(records, x => x.Id.Id == EventIds.PumpFailed);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Same(failure, record.Exception);
    }

    #endregion
}
