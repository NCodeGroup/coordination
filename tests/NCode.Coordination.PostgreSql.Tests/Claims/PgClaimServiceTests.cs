// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Claims.Tests;

public sealed class PgClaimServiceTests
{
    private static PgClaimService CreateService(PgOfflineServer offline)
    {
        var server = offline.Server;
        var keyspaces = offline.Keyspaces;
        var schema = offline.Settings.Schema;

        return new PgClaimService(server, keyspaces, schema);
    }

    private static TimeWindow CreateWindow() =>
        new(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(1));

    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_NameAtTheSegmentLimit_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var segments = Enumerable.Repeat<KeySegment>("a", CoordinationKey.MaxSegments);
        var name = new Keyspace("prod").CreateKey(segments);

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
    }

    [Fact]
    public async Task TryClaimAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = new Keyspace("prod").CreateKey("k");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

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
    }

    #endregion
}
