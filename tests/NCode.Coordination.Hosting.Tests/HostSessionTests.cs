// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class HostSessionTests
{
    private static SessionId CreateId(string value) => new(new Keyspace("hosting"), value);

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheIdIsNull_Throws()
    {
        var member = new Member(CreateId("session-1"), "node-1", "10.0.0.1:5000");

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new HostSession(null!, member, CancellationToken.None)
        );

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheMemberIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new HostSession(CreateId("session-1"), null!, CancellationToken.None)
        );

        Assert.Equal("member", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheMemberBelongsToAnotherSession_Throws()
    {
        var member = new Member(CreateId("session-2"), "node-1", "10.0.0.1:5000");

        var exception = Assert.Throws<ArgumentException>(() =>
            new HostSession(CreateId("session-1"), member, CancellationToken.None)
        );

        Assert.Equal("member", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheArgumentsAreValid_KeepsThem()
    {
        var id = CreateId("session-1");
        var member = new Member(id, "node-1", "10.0.0.1:5000");
        using var source = new CancellationTokenSource();

        var session = new HostSession(id, member, source.Token);

        Assert.Same(id, session.Id);
        Assert.Same(member, session.Member);
        Assert.Equal(source.Token, session.Ended);
    }

    #endregion
}
