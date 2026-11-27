// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Claims.Tests;

public sealed class PgClaimServiceTests
{
    private static async Task WithServiceAsync(Func<PgClaimService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var server = new PgServer(source, PgSettings.Default);
        var schema = PgSchemaName.Default;
        var keyspaces = new PgKeyspaces(server, schema);

        await test(new PgClaimService(server, keyspaces, schema));
    }

    private static TimeWindow CreateWindow() =>
        new(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(1));

    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_NameAtTheSegmentLimit_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var segments = Enumerable.Repeat<KeySegment>("a", CoordinationKey.MaxSegments);
        var name = new Keyspace("prod").CreateKey(segments);
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.TryClaimAsync(
                    name,
                    CreateWindow(),
                    new TimeToLive(TimeSpan.FromMinutes(1)),
                    null,
                    cancellationToken
                )
            );

            Assert.Equal("name", exception.ParamName);
        });
    }

    [Fact]
    public async Task TryClaimAsync_Extension_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.TryClaimAsync(
                    name,
                    CreateWindow(),
                    new TimeToLive(TimeSpan.FromMinutes(1)),
                    options,
                    cancellationToken
                )
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion
}
