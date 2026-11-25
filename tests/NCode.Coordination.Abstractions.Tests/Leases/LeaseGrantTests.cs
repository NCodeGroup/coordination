// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class LeaseGrantTests
{
    private static readonly Keyspace ProdKeyspace = new("prod");

    private static CoordinationKey Resource() => new(ProdKeyspace, "job");

    private static LeaseTerms Terms() =>
        LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)));

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsAll()
    {
        var resource = Resource();
        var fencingToken = new FencingToken(5);
        var owner = new OwnerToken("o-1");
        var terms = Terms();

        var grant = new LeaseGrant(resource, fencingToken, owner, terms);

        Assert.Same(resource, grant.Resource);
        Assert.Same(fencingToken, grant.FencingToken);
        Assert.Same(owner, grant.Owner);
        Assert.Same(terms, grant.Terms);
    }

    [Fact]
    public void Constructor_WhenResourceIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new LeaseGrant(null!, new FencingToken(5), new OwnerToken("o-1"), Terms())
        );

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenFencingTokenIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new LeaseGrant(Resource(), null!, new OwnerToken("o-1"), Terms())
        );

        Assert.Equal("fencingToken", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenOwnerIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new LeaseGrant(Resource(), new FencingToken(5), null!, Terms())
        );

        Assert.Equal("owner", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTermsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new LeaseGrant(Resource(), new FencingToken(5), new OwnerToken("o-1"), null!)
        );

        Assert.Equal("terms", exception.ParamName);
    }

    #endregion
}
