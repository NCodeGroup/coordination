// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Sets.Tests;

public sealed class PgSetServiceTests
{
    private static async Task WithServiceAsync(Func<PgSetService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var server = new PgServer(source, PgSettings.Default);
        var schema = PgSchemaName.Default;
        var keyspaces = new PgKeyspaces(server, schema);

        await test(new PgSetService(server, keyspaces, schema));
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_NullMember_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new Keyspace("prod").CreateKey("k");
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await service.AddAsync(
                    key,
                    null!,
                    new TimeToLive(TimeSpan.FromSeconds(5)),
                    null,
                    cancellationToken
                )
            );
        });
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_Extension_ThrowsUnsupported()
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
                await service.ListAsync(key, options, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion
}
