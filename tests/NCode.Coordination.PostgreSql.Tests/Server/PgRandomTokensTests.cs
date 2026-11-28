// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgRandomTokensTests
{
    #region Next Tests

    [Fact]
    public void Next_WithAPrefix_AppendsThirtyTwoLowercaseHexDigits()
    {
        var tokens = new PgRandomTokens();

        var token = tokens.Next("s-");

        Assert.Matches("^s-[0-9a-f]{32}$", token);
    }

    [Fact]
    public void Next_CalledTwice_GivesDifferentTokens()
    {
        var tokens = new PgRandomTokens();

        var first = tokens.Next("o-");
        var second = tokens.Next("o-");

        Assert.NotEqual(first, second);
    }

    #endregion

    #region NextKeyspaceId Tests

    [Fact]
    public void NextKeyspaceId_CalledRepeatedly_IsNeverZero()
    {
        var tokens = new PgRandomTokens();

        for (var draw = 0; draw < 100; draw++)
        {
            var id = tokens.NextKeyspaceId();

            Assert.NotEqual(0L, id);
        }
    }

    #endregion
}
