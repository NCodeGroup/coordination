// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SemaphoreGrantTests
{
    private static readonly Keyspace ProdKeyspace = new("prod");

    private static CoordinationKey Resource() => new(ProdKeyspace, "pool");

    private static LeaseTerms Terms()
    {
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        return LeaseTerms.ForTimeToLive(timeToLive);
    }

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
        var resource = Resource();
        var terms = Terms();

        var grant = new SemaphoreGrant(
            resource,
            0,
            new FencingToken(5),
            new OwnerToken("o-1"),
            terms
        );

        Assert.Equal(0, grant.Slot);
    }

    [Fact]
    public void Constructor_WhenSlotIsTheLargestCapacityLessOne_Accepts()
    {
        var resource = Resource();
        var terms = Terms();
        const int slot = CoordinationLimits.MaxSemaphoreCapacity - 1;

        var grant = new SemaphoreGrant(
            resource,
            slot,
            new FencingToken(5),
            new OwnerToken("o-1"),
            terms
        );

        Assert.Equal(slot, grant.Slot);
    }

    [Fact]
    public void Constructor_WhenResourceIsNull_Throws()
    {
        var terms = Terms();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(null!, 0, new FencingToken(5), new OwnerToken("o-1"), terms)
        );

        Assert.Equal("resource", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenSlotIsNegative_Throws()
    {
        var resource = Resource();
        var terms = Terms();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemaphoreGrant(resource, -1, new FencingToken(5), new OwnerToken("o-1"), terms)
        );

        Assert.Equal("slot", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenSlotIsTheLargestCapacity_Throws()
    {
        var resource = Resource();
        var terms = Terms();
        const int slot = CoordinationLimits.MaxSemaphoreCapacity;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemaphoreGrant(resource, slot, new FencingToken(5), new OwnerToken("o-1"), terms)
        );

        Assert.Equal("slot", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenFencingTokenIsNull_Throws()
    {
        var resource = Resource();
        var terms = Terms();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(resource, 0, null!, new OwnerToken("o-1"), terms)
        );

        Assert.Equal("fencingToken", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenOwnerIsNull_Throws()
    {
        var resource = Resource();
        var terms = Terms();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(resource, 0, new FencingToken(5), null!, terms)
        );

        Assert.Equal("owner", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTermsAreNull_Throws()
    {
        var resource = Resource();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SemaphoreGrant(resource, 0, new FencingToken(5), new OwnerToken("o-1"), null!)
        );

        Assert.Equal("terms", exception.ParamName);
    }

    #endregion
}
