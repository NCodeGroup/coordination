// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecordLifetimeTests
{
    #region Persistent Property Tests

    [Fact]
    public void Persistent_Always_HasNoTimeToLiveAndNoSession()
    {
        var lifetime = RecordLifetime.Persistent;

        Assert.Null(lifetime.TimeToLive);
        Assert.Null(lifetime.Session);
    }

    [Fact]
    public void Persistent_Always_IsOneSharedInstance()
    {
        Assert.Same(RecordLifetime.Persistent, RecordLifetime.Persistent);
    }

    #endregion

    #region ForTimeToLive Tests

    [Fact]
    public void ForTimeToLive_WhenTimeToLiveIsGiven_KeepsTimeToLiveAndNoSession()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));

        var lifetime = RecordLifetime.ForTimeToLive(timeToLive);

        Assert.Same(timeToLive, lifetime.TimeToLive);
        Assert.Null(lifetime.Session);
    }

    [Fact]
    public void ForTimeToLive_WhenTimeToLiveIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordLifetime.ForTimeToLive(null!)
        );

        Assert.Equal("timeToLive", exception.ParamName);
    }

    #endregion

    #region ForSession Tests

    [Fact]
    public void ForSession_WhenSessionIsGiven_KeepsSessionAndNoTimeToLive()
    {
        var session = new SessionId(new Keyspace("prod"), "7");

        var lifetime = RecordLifetime.ForSession(session);

        Assert.Same(session, lifetime.Session);
        Assert.Null(lifetime.TimeToLive);
    }

    [Fact]
    public void ForSession_WhenSessionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordLifetime.ForSession(null!)
        );

        Assert.Equal("session", exception.ParamName);
    }

    #endregion

    #region IsPersistent Property Tests

    [Fact]
    public void IsPersistent_WhenPersistent_ReturnsTrue()
    {
        Assert.True(RecordLifetime.Persistent.IsPersistent);
    }

    [Fact]
    public void IsPersistent_WhenForTimeToLive_ReturnsFalse()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));
        var lifetime = RecordLifetime.ForTimeToLive(timeToLive);

        Assert.False(lifetime.IsPersistent);
    }

    [Fact]
    public void IsPersistent_WhenForSession_ReturnsFalse()
    {
        var session = new SessionId(new Keyspace("prod"), "7");
        var lifetime = RecordLifetime.ForSession(session);

        Assert.False(lifetime.IsPersistent);
    }

    #endregion
}
