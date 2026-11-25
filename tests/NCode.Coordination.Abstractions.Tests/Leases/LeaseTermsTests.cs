// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class LeaseTermsTests
{
    #region ForSession Tests

    [Fact]
    public void ForSession_WhenSessionIsGiven_KeepsSessionAndNoTimeToLive()
    {
        var session = new SessionId(new Keyspace("prod"), "7");

        var terms = LeaseTerms.ForSession(session);

        Assert.Same(session, terms.Session);
        Assert.Null(terms.TimeToLive);
    }

    [Fact]
    public void ForSession_WhenSessionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => LeaseTerms.ForSession(null!));

        Assert.Equal("session", exception.ParamName);
    }

    #endregion

    #region ForTimeToLive Tests

    [Fact]
    public void ForTimeToLive_WhenTimeToLiveIsGiven_KeepsTimeToLiveAndNoSession()
    {
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);

        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        Assert.Same(timeToLive, terms.TimeToLive);
        Assert.Null(terms.Session);
    }

    [Fact]
    public void ForTimeToLive_WhenTimeToLiveIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => LeaseTerms.ForTimeToLive(null!));

        Assert.Equal("timeToLive", exception.ParamName);
    }

    #endregion

    #region IsBoundToSession Property Tests

    [Fact]
    public void IsBoundToSession_WhenForSession_ReturnsTrue()
    {
        var session = new SessionId(new Keyspace("prod"), "7");
        var terms = LeaseTerms.ForSession(session);

        Assert.True(terms.IsBoundToSession);
    }

    [Fact]
    public void IsBoundToSession_WhenForTimeToLive_ReturnsFalse()
    {
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);

        Assert.False(terms.IsBoundToSession);
    }

    #endregion
}
