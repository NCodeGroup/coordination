// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgChangeStartTests
{
    private const long KeyspaceId = 7;

    private static DateTime Now { get; } = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    private static PgKeyspaceChange CreateChange()
    {
        var origin = new PgChangeOrigin(KeyspaceId, 5, Now);
        return new PgKeyspaceChange(PgSettings.Default, origin);
    }

    private static PgChangeStart CreateStart(PgKeyspaceChange change, DateTime sessionDeadline)
    {
        var sessions = ImmutableDictionary<string, DateTime>.Empty.Add("s-1", sessionDeadline);
        var rows = ImmutableDictionary<CoordinationKey, PgRecordRow>.Empty;
        return new PgChangeStart(change, rows, sessions);
    }

    #region IsLiveSession Tests

    [Fact]
    public void IsLiveSession_DeadlineAtTheInstant_ReturnsFalse()
    {
        var change = CreateChange();
        var start = CreateStart(change, Now);

        var live = start.IsLiveSession("s-1");

        Assert.False(live);
    }

    [Fact]
    public void IsLiveSession_DeadlineAfterTheInstant_ReturnsTrue()
    {
        var change = CreateChange();
        var after = Now.AddTicks(1);
        var start = CreateStart(change, after);

        var live = start.IsLiveSession("s-1");

        Assert.True(live);
    }

    #endregion

    #region EndExpiredSessions Tests

    [Fact]
    public void EndExpiredSessions_DeadlineAtTheInstant_EndsTheSession()
    {
        var change = CreateChange();
        var start = CreateStart(change, Now);

        start.EndExpiredSessions();

        Assert.Equal(6, change.Revision);
    }

    [Fact]
    public void EndExpiredSessions_DeadlineAfterTheInstant_EndsNothing()
    {
        var change = CreateChange();
        var after = Now.AddTicks(1);
        var start = CreateStart(change, after);

        start.EndExpiredSessions();

        Assert.Equal(5, change.Revision);
        Assert.True(change.IsEmpty);
    }

    #endregion
}
