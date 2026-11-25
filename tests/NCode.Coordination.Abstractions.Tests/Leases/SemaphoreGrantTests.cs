// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SemaphoreGrantTests
{
    private static readonly Keyspace ProdKeyspace = new("prod");

    private static CoordinationKey Resource() => new(ProdKeyspace, "pool");

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

        var grant = new SemaphoreGrant(resource, 3, fencingToken, owner, terms);

        Assert.Same(resource, grant.Resource);
        Assert.Equal(3, grant.Slot);
        Assert.Same(fencingToken, grant.FencingToken);
        Assert.Same(owner, grant.Owner);
        Assert.Same(terms, grant.Terms);
    }

    [Fact]
    public void Constructor_WhenSlotIsZero_Accepts()
    {
        var grant = new SemaphoreGrant(
            Resource(),
            0,
            new FencingToken(5),
            new OwnerToken("o-1"),
            Terms()
        );

        Assert.Equal(0, grant.Slot);
    }

    [Fact]
    public void Constructor_WhenSlotIsTheLargestCapacityLessOne_Accepts()
    {
        var grant = new SemaphoreGrant(
            Resource(),
            CoordinationLimits.MaxSemaphoreCapacity - 1,
            new FencingToken(5),
            new OwnerToken("o-1"),
            Terms()
        );

        Assert.Equal(1_023, grant.Slot);
    }

    [Fact]
    public void Constructor_WhenResourceIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(null!, 0, new FencingToken(5), new OwnerToken("o-1"), Terms())
        );

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenSlotIsNegative_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemaphoreGrant(Resource(), -1, new FencingToken(5), new OwnerToken("o-1"), Terms())
        );

        Assert.Equal("slot", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenSlotIsTheLargestCapacity_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemaphoreGrant(
                Resource(),
                1_024,
                new FencingToken(5),
                new OwnerToken("o-1"),
                Terms()
            )
        );

        Assert.Equal("slot", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenFencingTokenIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(Resource(), 0, null!, new OwnerToken("o-1"), Terms())
        );

        Assert.Equal("fencingToken", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenOwnerIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(Resource(), 0, new FencingToken(5), null!, Terms())
        );

        Assert.Equal("owner", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTermsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(Resource(), 0, new FencingToken(5), new OwnerToken("o-1"), null!)
        );

        Assert.Equal("terms", exception.ParamName);
    }

    #endregion
}
