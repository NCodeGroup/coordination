// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessSessionIndexTests
{
    private static SessionId CreateSession(string token)
    {
        return new SessionId(InProcessTesting.Keyspace, token);
    }

    #region Take Tests

    [Fact]
    public void Take_WhenItemsAreBound_ReturnsThemAndForgetsTheSession()
    {
        var index = new InProcessSessionIndex<string>();
        var session = CreateSession("s1");

        index.Bind(session, "a");
        index.Bind(session, "b");

        var first = index.Take(session);
        var second = index.Take(session);

        Assert.Equal(2, first.Length);
        Assert.Contains("a", first);
        Assert.Contains("b", first);
        Assert.Empty(second);
    }

    [Fact]
    public void Take_WhenNothingIsBound_ReturnsEmpty()
    {
        var index = new InProcessSessionIndex<string>();
        var session = CreateSession("s1");

        var taken = index.Take(session);

        Assert.Empty(taken);
    }

    #endregion

    #region Unbind Tests

    [Fact]
    public void Unbind_WhenTheLastItemLeaves_ForgetsTheSession()
    {
        var index = new InProcessSessionIndex<string>();
        var session = CreateSession("s1");

        index.Bind(session, "a");
        index.Unbind(session, "a");

        var taken = index.Take(session);

        Assert.Empty(taken);
    }

    [Fact]
    public void Unbind_AfterTake_DoesNothing()
    {
        var index = new InProcessSessionIndex<string>();
        var session = CreateSession("s1");

        index.Bind(session, "a");
        index.Take(session);

        index.Unbind(session, "a");

        var taken = index.Take(session);

        Assert.Empty(taken);
    }

    #endregion
}
