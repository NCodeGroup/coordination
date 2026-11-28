// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Sequences.Tests;

public sealed class PgSequenceServiceTests
{
    private static PgSequenceService CreateService(PgOfflineServer offline)
    {
        var server = offline.Server;
        var keyspaces = offline.Keyspaces;
        var schema = offline.Settings.Schema;

        return new PgSequenceService(server, keyspaces, schema);
    }

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_CountZero_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.ReserveAsync(key, 0, null, cancellationToken)
        );
    }

    [Fact]
    public async Task ReserveAsync_CountAboveTheLimit_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.ReserveAsync(
                key,
                CoordinationLimits.MaxSequenceBlock + 1,
                null,
                cancellationToken
            )
        );
    }

    #endregion

    #region NextAsync Tests

    [Fact]
    public async Task NextAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s");
        var options = new OperationOptions { Extensions = extensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.NextAsync(key, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
