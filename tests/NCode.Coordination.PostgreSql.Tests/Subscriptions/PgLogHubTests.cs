// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Subscriptions.Tests;

public sealed class PgLogHubTests
{
    private const long KeyspaceId = 42;

    private static string Channel { get; } =
        PgChannels.ForKeyspace(PgSchemaName.Default, KeyspaceId);

    private static Dictionary<string, long> Channels { get; } = new() { [Channel] = KeyspaceId };

    #region TryRoute Tests

    [Fact]
    public void TryRoute_ChangePayload_RoutesToTheChangeLog()
    {
        var payload = PgChannels.ChangePayload(7);

        var routed = PgLogHub.TryRoute(Channels, Channel, payload, out var id);

        Assert.True(routed);
        var expected = new PgLogId(KeyspaceId, null);
        Assert.Equal(expected, id);
    }

    [Fact]
    public void TryRoute_TopicPayload_RoutesToTheTopic()
    {
        var payload = PgChannels.TopicPayload("abc", 3);

        var routed = PgLogHub.TryRoute(Channels, Channel, payload, out var id);

        Assert.True(routed);
        var expected = new PgLogId(KeyspaceId, "abc");
        Assert.Equal(expected, id);
    }

    [Fact]
    public void TryRoute_UnknownChannel_ReturnsFalse()
    {
        var other = PgChannels.ForKeyspace(PgSchemaName.Default, KeyspaceId + 1);
        var payload = PgChannels.ChangePayload(7);

        var routed = PgLogHub.TryRoute(Channels, other, payload, out var id);

        Assert.False(routed);
        Assert.Equal(default, id);
    }

    [Fact]
    public void TryRoute_MalformedPayload_ReturnsFalse()
    {
        var routed = PgLogHub.TryRoute(Channels, Channel, "x:1", out var id);

        Assert.False(routed);
        Assert.Equal(default, id);
    }

    #endregion
}
