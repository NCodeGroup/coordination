// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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

    private static TimeWindow CreateWindow()
    {
        var length = TimeSpan.FromHours(1);

        return new TimeWindow(DateTimeOffset.UnixEpoch, length);
    }

    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_NameAtTheSegmentLimit_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var segments = Enumerable.Repeat<KeySegment>("a", CoordinationKey.MaxSegments);
        var name = new Keyspace("prod").CreateKey(segments);
        var window = CreateWindow();
        var duration = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(duration);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.TryClaimAsync(name, window, timeToLive, null, cancellationToken)
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task TryClaimAsync_NameBelowTheSegmentLimit_ReachesTheServer()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        const int count = CoordinationKey.MaxSegments - 1;
        var segments = Enumerable.Repeat<KeySegment>("a", count);
        var name = new Keyspace("prod").CreateKey(segments);
        var window = CreateWindow();
        var duration = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(duration);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryClaimAsync(name, window, timeToLive, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    [Fact]
    public async Task TryClaimAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var name = new Keyspace("prod").CreateKey("k");
        var window = CreateWindow();
        var duration = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(duration);
        var options = new OperationOptions { Extensions = PgTestSupport.UnsupportedExtensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryClaimAsync(name, window, timeToLive, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
