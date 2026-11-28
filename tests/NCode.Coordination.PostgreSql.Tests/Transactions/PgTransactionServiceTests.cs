// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Transactions.Tests;

public sealed class PgTransactionServiceTests
{
    private static PgTransactionService CreateService(PgOfflineServer offline)
    {
        var table = offline.Table;

        return new PgTransactionService(table);
    }

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_NullTransaction_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.ExecuteAsync(null!, null, cancellationToken)
        );
    }

    [Fact]
    public async Task ExecuteAsync_SessionInTheFailureBranch_ReachesTheServer()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
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

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ExecuteAsync(transaction, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    #endregion
}
