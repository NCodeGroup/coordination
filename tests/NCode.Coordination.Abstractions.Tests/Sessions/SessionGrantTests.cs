// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SessionGrantTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsBoth()
    {
        var id = new SessionId(new Keyspace("prod"), "7");
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);

        var grant = new SessionGrant(id, timeToLive);

        Assert.Same(id, grant.Id);
        Assert.Same(timeToLive, grant.TimeToLive);
    }

    [Fact]
    public void Constructor_WhenIdIsNull_Throws()
    {
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SessionGrant(null!, timeToLive)
        );

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTimeToLiveIsNull_Throws()
    {
        var id = new SessionId(new Keyspace("prod"), "7");

        var exception = Assert.Throws<ArgumentNullException>(() => new SessionGrant(id, null!));

        Assert.Equal("timeToLive", exception.ParamName);
    }

    #endregion
}
