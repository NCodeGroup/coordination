// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespRandomTokensTests
{
    #region Next Tests

    [Fact]
    public void Next_WithAPrefix_AppendsThirtyTwoLowercaseHexDigits()
    {
        var tokens = new RespRandomTokens();

        var token = tokens.Next("s-");

        Assert.Matches("^s-[0-9a-f]{32}$", token);
    }

    [Fact]
    public void Next_CalledTwice_GivesDifferentTokens()
    {
        var tokens = new RespRandomTokens();

        var first = tokens.Next("o-");
        var second = tokens.Next("o-");

        Assert.NotEqual(first, second);
    }

    #endregion
}
