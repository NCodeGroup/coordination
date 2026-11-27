// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Records;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Transactions.Tests;

public sealed class PgTransactionServiceTests
{
    private static async Task WithServiceAsync(Func<PgTransactionService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, settings.Schema);

        await test(new PgTransactionService(new PgRecordTable(server, keyspaces, settings)));
    }

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_NullTransaction_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await service.ExecuteAsync(null!, null, cancellationToken)
            );
        });
    }

    [Fact]
    public async Task ExecuteAsync_SessionInTheFailureBranch_ReachesTheServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var keyspace = new Keyspace("prod");
        var key = keyspace.CreateKey("k");
        var other = keyspace.CreateKey("other");
        var transaction = new RecordTransaction(
            [RecordCondition.Absent(key)],
            [RecordWrite.Put(key, new byte[1], RecordLifetime.Persistent)]
        )
        {
            OnFailure =
            [
                RecordWrite.Put(
                    other,
                    new byte[1],
                    RecordLifetime.ForSession(new SessionId(keyspace, "s1"))
                ),
            ],
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.ExecuteAsync(transaction, null, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        });
    }

    #endregion
}
