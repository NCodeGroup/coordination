// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Occurrences.Tests;

public sealed class PgOccurrenceServiceTests
{
    private static PgOccurrenceService CreateService(PgOfflineServer offline)
    {
        var server = offline.Server;
        var keyspaces = offline.Keyspaces;
        var schema = offline.Settings.Schema;

        return new PgOccurrenceService(server, keyspaces, schema);
    }

    #region TryRecordAsync Tests

    [Fact]
    public async Task TryRecordAsync_NullKey_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.TryRecordAsync(
                null!,
                new TimeToLive(TimeSpan.FromSeconds(5)),
                null,
                cancellationToken
            )
        );
    }

    [Fact]
    public async Task TryRecordAsync_Extension_ThrowsUnsupportedBeforeTheServer()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryRecordAsync(
                key,
                new TimeToLive(TimeSpan.FromSeconds(5)),
                options,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    [Fact]
    public async Task TryRecordAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var key = new Keyspace("prod").CreateKey("k");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.TryRecordAsync(
                key,
                new TimeToLive(TimeSpan.FromSeconds(5)),
                null,
                cts.Token
            )
        );
    }

    #endregion
}
