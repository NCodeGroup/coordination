// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Occurrences.Tests;

public sealed class PgOccurrenceServiceTests
{
    private static async Task WithServiceAsync(Func<PgOccurrenceService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var server = new PgServer(source, PgSettings.Default);
        var schema = PgSchemaName.Default;
        var keyspaces = new PgKeyspaces(server, schema);

        await test(new PgOccurrenceService(server, keyspaces, schema));
    }

    #region TryRecordAsync Tests

    [Fact]
    public async Task TryRecordAsync_NullKey_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await service.TryRecordAsync(
                    null!,
                    new TimeToLive(TimeSpan.FromSeconds(5)),
                    null,
                    cancellationToken
                )
            );
        });
    }

    [Fact]
    public async Task TryRecordAsync_Extension_ThrowsUnsupportedBeforeTheServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
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
        });
    }

    [Fact]
    public async Task TryRecordAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var key = new Keyspace("prod").CreateKey("k");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await service.TryRecordAsync(
                    key,
                    new TimeToLive(TimeSpan.FromSeconds(5)),
                    null,
                    cts.Token
                )
            );
        });
    }

    #endregion
}
