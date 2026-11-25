// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class MemberTests
{
    private static SessionId NewSession() => new(new Keyspace("prod"), "7");

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsTheValues()
    {
        var session = NewSession();

        var member = new Member(session, "node-1", "10.0.0.4:5000");

        Assert.Same(session, member.Session);
        Assert.Equal("node-1", member.NodeId);
        Assert.Equal("10.0.0.4:5000", member.Address);
    }

    [Fact]
    public void Constructor_WhenAddressHasTheMaximumLength_Accepts()
    {
        var session = NewSession();
        var address = new string('a', CoordinationLimits.MaxAddressLength);

        var member = new Member(session, "node-1", address);

        Assert.Equal(address, member.Address);
    }

    [Fact]
    public void Constructor_WhenSessionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Member(null!, "node-1", "10.0.0.4:5000")
        );

        Assert.Equal("session", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenNodeIdIsNotAToken_Throws()
    {
        var session = NewSession();

        var exception = Assert.Throws<ArgumentException>(() =>
            new Member(session, "a b", "10.0.0.4:5000")
        );

        Assert.Equal("nodeId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAddressIsWhitespace_Throws()
    {
        var session = NewSession();

        var exception = Assert.Throws<ArgumentException>(() => new Member(session, "node-1", " "));

        Assert.Equal("address", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAddressIsTooLong_Throws()
    {
        var session = NewSession();

        var exception = Assert.Throws<ArgumentException>(() =>
            new Member(session, "node-1", new string('a', CoordinationLimits.MaxAddressLength + 1))
        );

        Assert.Equal("address", exception.ParamName);
    }

    #endregion

    #region Metadata Property Tests

    [Fact]
    public void Metadata_ByDefault_IsEmpty()
    {
        var session = NewSession();

        var member = new Member(session, "node-1", "10.0.0.4:5000");

        Assert.Empty(member.Metadata);
    }

    [Fact]
    public void Metadata_WhenValid_IsKept()
    {
        var session = NewSession();

        var member = new Member(session, "node-1", "10.0.0.4:5000")
        {
            Metadata = ImmutableDictionary<string, string>.Empty.Add("app.zone", "a"),
        };

        Assert.Equal("a", member.Metadata["app.zone"]);
    }

    [Fact]
    public void Metadata_WhenThereAreTheMaximumEntries_Accepts()
    {
        var values = Enumerable
            .Range(0, CoordinationLimits.MaxMetadataEntries)
            .ToImmutableDictionary(i => $"x.n{i}", _ => "v");

        var session = NewSession();

        var member = new Member(session, "node-1", "10.0.0.4:5000") { Metadata = values };

        Assert.Equal(CoordinationLimits.MaxMetadataEntries, member.Metadata.Count);
    }

    [Fact]
    public void Metadata_WhenThereAreTooMany_Throws()
    {
        var values = Enumerable
            .Range(0, CoordinationLimits.MaxMetadataEntries + 1)
            .ToImmutableDictionary(i => $"x.n{i}", _ => "v");

        var session = NewSession();

        var exception = Assert.Throws<ArgumentException>(() =>
            new Member(session, "node-1", "10.0.0.4:5000") { Metadata = values }
        );

        Assert.Equal("Metadata", exception.ParamName);
    }

    #endregion
}
