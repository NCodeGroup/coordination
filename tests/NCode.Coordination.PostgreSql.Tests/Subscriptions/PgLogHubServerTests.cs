// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Subscriptions.Tests;

public sealed class PgLogHubServerTests(PgServers servers)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private sealed record TestEntry(long Number) : IPgLogEntry;

    #region CheckAsync Tests

    [PgServerFact]
    public async Task CheckAsync_PumpThrowsUnexpectedly_NextCheckStillDelivers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var schemaName = "hub_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var schema = PgSchemaName.Create(schemaName);
        var settings = PgSettings.Default with
        {
            Schema = schema,
            CheckInterval = TimeSpan.FromSeconds(1),
        };
        var calls = 0;
        var id = new PgLogId(1, null);

        ValueTask<PgLogRead<TestEntry>> Read(long after, int count, CancellationToken token)
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                return ValueTask.FromResult(new PgLogRead<TestEntry>(0, 0, []));
            }

            if (call == 2)
            {
                return ValueTask.FromException<PgLogRead<TestEntry>>(
                    new InvalidOperationException("The read failed unexpectedly.")
                );
            }

            return ValueTask.FromResult(new PgLogRead<TestEntry>(0, 1, [new TestEntry(1)]));
        }

        try
        {
            await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
            var server = new PgServer(source, settings);
            var probe = new PgProbe(server, source, PostgreSqlProfiles.Standalone, settings);
            await probe.RunAsync(cancellationToken);

            var keyspaces = new PgKeyspaces(server, schema, new PgRandomTokens());
            await using var hub = new PgLogHub(source, keyspaces, settings, TimeProvider.System);

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
    }

    #endregion
}
