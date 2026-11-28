// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgLivenessTests
{
    private static DateTime Now { get; } = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    #region StateOf Tests

    [Theory]
    [InlineData(-1, null, null, "Expired")]
    [InlineData(0, null, null, "Expired")]
    [InlineData(1, "s-1", null, "SessionEnded")]
    [InlineData(1, "s-1", -1, "SessionExpired")]
    [InlineData(1, "s-1", 0, "SessionExpired")]
    [InlineData(1, "s-1", 1, "Live")]
    [InlineData(1, null, null, "Live")]
    public void StateOf_Deadlines_ReturnsTheState(
        int deadlineSeconds,
        string? session,
        int? sessionDeadlineSeconds,
        string expected
    )
    {
        DateTime? sessionDeadline = sessionDeadlineSeconds is { } seconds
            ? Now.AddSeconds(seconds)
            : null;

        var deadline = Now.AddSeconds(deadlineSeconds);

        var state = PgLiveness.StateOf(deadline, session, sessionDeadline, Now);

        var stateName = state.ToString();
        Assert.Equal(expected, stateName);
    }

    #endregion
}
