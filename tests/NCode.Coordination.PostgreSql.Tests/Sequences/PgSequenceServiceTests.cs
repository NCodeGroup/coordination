// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Sequences.Tests;

public sealed class PgSequenceServiceTests
{
    private static async Task WithServiceAsync(Func<PgSequenceService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var server = new PgServer(source, PgSettings.Default);
        var schema = PgSchemaName.Default;
        var keyspaces = new PgKeyspaces(server, schema);

        await test(new PgSequenceService(server, keyspaces, schema));
    }

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_CountZero_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.ReserveAsync(key, 0, null, cancellationToken)
            );
        });
    }

    [Fact]
    public async Task ReserveAsync_CountAboveTheLimit_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await service.ReserveAsync(
                    key,
                    CoordinationLimits.MaxSequenceBlock + 1,
                    null,
                    cancellationToken
                )
            );
        });
    }

    #endregion

    #region NextAsync Tests

    [Fact]
    public async Task NextAsync_Extension_ThrowsUnsupported()
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
                await service.NextAsync(key, options, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion
}
