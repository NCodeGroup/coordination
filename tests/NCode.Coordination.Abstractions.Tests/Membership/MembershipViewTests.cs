// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class MembershipViewTests
{
    private static readonly Keyspace ProdKeyspace = new("prod");

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsTheValues()
    {
        var group = new CoordinationKey(ProdKeyspace, "workers");
        var revision = new Revision(5);
        var first = new Member(new SessionId(ProdKeyspace, "1"), "node-1", "10.0.0.4:5000");
        var second = new Member(new SessionId(ProdKeyspace, "2"), "node-2", "10.0.0.5:5000");

        var view = new MembershipView(group, revision, first, second);

        Assert.Same(group, view.Group);
        Assert.Same(revision, view.Revision);
        Assert.Equal(2, view.Members.Length);
        Assert.Same(first, view.Members[0]);
        Assert.Same(second, view.Members[1]);
    }

    [Fact]
    public void Constructor_WhenThereAreNoMembers_IsEmpty()
    {
        var view = new MembershipView(
            new CoordinationKey(ProdKeyspace, "workers"),
            new Revision(1)
        );

        Assert.Empty(view.Members);
    }

    [Fact]
    public void Constructor_WhenGroupIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MembershipView(null!, new Revision(1))
        );

        Assert.Equal("group", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenRevisionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MembershipView(new CoordinationKey(ProdKeyspace, "workers"), null!)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAMemberIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MembershipView(
                new CoordinationKey(ProdKeyspace, "workers"),
                new Revision(1),
                new Member[] { null! }
            )
        );

        Assert.Equal("members", exception.ParamName);
    }

    #endregion
}
