// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Tests;
using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Sequences.Tests;

public sealed class PgSequenceServiceServerTests(PgServers servers)
{
    private async Task WithSequenceNearTheLargestValueAsync(
        Func<ISequenceService, CoordinationKey, Task> test,
        CancellationToken cancellationToken
    )
    {
        var guid = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var name = "t_" + guid;
        var schema = PgSchemaName.Create(name);

        try
        {
            await using var provider = await PgTestSupport.CreateProviderAsync(
                servers.PostgreSql19,
                PostgreSqlProfiles.Standalone,
                schema,
                cancellationToken
            );
            var sequences = PgTestSupport.GetService<ISequenceService>(provider);
            var keyspace = PgTestSupport.CreateKeyspace("sequences");
            var key = keyspace.CreateKey("orders");
            await sequences.NextAsync(key, null, cancellationToken);
            var seed = (long.MaxValue - 1).ToString(CultureInfo.InvariantCulture);
            var sql = $"UPDATE \"{name}\".sequences SET value = {seed}";
            await PgServers.ExecuteAsAdminAsync(servers.Admin19, sql, cancellationToken);

            await test(sequences, key);
        }
        finally
        {
            await PgServers.ExecuteAsAdminAsync(
                servers.Admin19,
                $"DROP SCHEMA IF EXISTS \"{name}\" CASCADE",
                CancellationToken.None
            );
        }
    }

    #region ReserveAsync Tests

    [PgServerFact]
    public async Task ReserveAsync_PastTheLargestValue_ThrowsLimitExceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSequenceNearTheLargestValueAsync(
            async (sequences, key) =>
            {
                var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                    await sequences.ReserveAsync(key, 2, null, cancellationToken)
                );
                var next = await sequences.NextAsync(key, null, cancellationToken);

                Assert.Equal(CoordinationErrorCodes.LimitExceeded, exception.Code);
                Assert.False(exception.OutcomeUnknown);
                Assert.Equal(long.MaxValue, next);
            },
            cancellationToken
        );
    }

    #endregion

    #region NextAsync Tests

    [PgServerFact]
    public async Task NextAsync_ToTheLargestValue_ReturnsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithSequenceNearTheLargestValueAsync(
            async (sequences, key) =>
            {
                var next = await sequences.NextAsync(key, null, cancellationToken);

                Assert.Equal(long.MaxValue, next);
            },
            cancellationToken
        );
    }

    #endregion
}
