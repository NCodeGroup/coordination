// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Tests;

/// <summary>
/// Proves what a PostgreSQL provider keeps and loses when a standby is promoted, each test failing over servers of its
/// own.
/// </summary>
public sealed class PostgreSqlProviderFailoverTests
{
    /// <summary>
    /// The name of the keyspaces that the tests create, which tells their keys apart from other tests'.
    /// </summary>
    private const string KeyspaceName = "failover";

    /// <summary>
    /// The number of records that the tests write at each step.
    /// </summary>
    private const int Records = 5;

    #region ProbeAsync Tests

    [PgServerFact]
    public async Task ProbeAsync_Standby_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(false, cancellationToken);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
        {
            await using var provider = await PgTestSupport.CreateProviderAsync(
                server.StandbyAdmin,
                PostgreSqlProfiles.Asynchronous,
                cancellationToken
            );
        });

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region CreateAsync Tests

    [PgServerFact]
    public async Task CreateAsync_WhenTheStandbyIsPromotedAfterReplicationIsCut_LosesOnlyTheWritesAfterTheCut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await PgFailoverServer.StartAsync(false, cancellationToken);
        await using var provider = await PgTestSupport.CreateProviderAsync(
            server.ConnectionString,
            PostgreSqlProfiles.Asynchronous,
            cancellationToken
        );
        var records = PgTestSupport.GetService<IRecordService>(provider);
        var keyspace = PgTestSupport.CreateKeyspace(KeyspaceName);
        var value = "v"u8.ToArray();

        var before = new List<CoordinationKey>();
        for (var index = 0; index < Records; index++)
        {
            var key = keyspace.CreateKey("before", index);
            var created = await records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );
            Assert.True(created.IsWritten);
            before.Add(key);
        }

        await server.WaitForStandbyAsync(cancellationToken);
        await server.CutReplicationAsync(cancellationToken);

        var lost = new List<CoordinationKey>();
        for (var index = 0; index < Records; index++)
        {
            var key = keyspace.CreateKey("lost", index);
            var created = await records.CreateAsync(
                key,
                value,
                RecordLifetime.Persistent,
                null,
                cancellationToken
            );
            Assert.True(created.IsWritten);
            lost.Add(key);
        }

        await server.PromoteStandbyAsync(cancellationToken);
        await server.StopPrimaryAsync(cancellationToken);

        var afterKey = keyspace.CreateKey("after");
        await PgTestSupport.WaitUntilAnsweredAsync(
            async ct =>
            {
                await records.CreateAsync(afterKey, value, RecordLifetime.Persistent, null, ct);
            },
            cancellationToken
        );

        foreach (var key in before)
        {
            var stored = await records.ReadAsync(key, null, cancellationToken);
            Assert.NotNull(stored);
        }

        foreach (var key in lost)
        {
            var stored = await records.ReadAsync(key, null, cancellationToken);
            Assert.Null(stored);
        }

        var after = await records.ReadAsync(afterKey, null, cancellationToken);
        Assert.NotNull(after);
    }

    #endregion
}
