// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class ElectionViewTests
{
    private static readonly Keyspace ProdKeyspace = new("prod");

    private static CoordinationKey Role(string name = "workers") => new(ProdKeyspace, name);

    private static Leader Leader(CoordinationKey role) =>
        new(role, new FencingToken(7), ReadOnlyMemory<byte>.Empty);

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenLeaderLeadsTheRole_KeepsBoth()
    {
        var role = Role();
        var leader = Leader(role);

        var view = new ElectionView(role, leader);

        Assert.Same(role, view.Role);
        Assert.Same(leader, view.Leader);
    }

    [Fact]
    public void Constructor_WhenLeaderIsNull_AcceptsAVacantRole()
    {
        var role = Role();

        var view = new ElectionView(role, null);

        Assert.Null(view.Leader);
    }

    [Fact]
    public void Constructor_WhenLeaderLeadsAnotherRole_Throws()
    {
        var other = Role("other");
        var leader = Leader(other);
        var role = Role();

        var exception = Assert.Throws<ArgumentException>(() => new ElectionView(role, leader));

        Assert.Equal("leader", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenRoleIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ElectionView(null!, null));

        Assert.Equal("role", exception.ParamName);
    }

    #endregion
}
