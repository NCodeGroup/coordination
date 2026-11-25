// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class LeaseAcquireResultTests
{
    private static LeaseGrant Grant() =>
        new(
            new CoordinationKey(new Keyspace("prod"), "job"),
            new FencingToken(5),
            new OwnerToken("o-1"),
            LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)))
        );

    #region Acquired Tests

    [Fact]
    public void Acquired_WhenGrantIsGiven_HasAcquiredStatusAndPayload()
    {
        var grant = Grant();

        var result = LeaseAcquireResult.Acquired(grant);

        Assert.Equal(LeaseAcquireStatus.Acquired, result.Status);
        Assert.Same(grant, result.Grant);
    }

    [Fact]
    public void Acquired_WhenGrantIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            LeaseAcquireResult.Acquired(null!)
        );

        Assert.Equal("grant", exception.ParamName);
    }

    #endregion

    #region Held Tests

    [Fact]
    public void Held_Always_HasHeldStatusAndNoPayload()
    {
        var result = LeaseAcquireResult.Held;

        Assert.Equal(LeaseAcquireStatus.Held, result.Status);
        Assert.Null(result.Grant);
    }

    [Fact]
    public void Held_Always_IsOneSharedInstance()
    {
        Assert.Same(LeaseAcquireResult.Held, LeaseAcquireResult.Held);
    }

    #endregion

    #region SessionEnded Tests

    [Fact]
    public void SessionEnded_Always_HasSessionEndedStatusAndNoPayload()
    {
        var result = LeaseAcquireResult.SessionEnded;

        Assert.Equal(LeaseAcquireStatus.SessionEnded, result.Status);
        Assert.Null(result.Grant);
    }

    [Fact]
    public void SessionEnded_Always_IsOneSharedInstance()
    {
        Assert.Same(LeaseAcquireResult.SessionEnded, LeaseAcquireResult.SessionEnded);
    }

    #endregion

    #region IsAcquired Property Tests

    [Fact]
    public void IsAcquired_WhenAcquired_ReturnsTrue()
    {
        Assert.True(LeaseAcquireResult.Acquired(Grant()).IsAcquired);
    }

    [Fact]
    public void IsAcquired_WhenHeld_ReturnsFalse()
    {
        Assert.False(LeaseAcquireResult.Held.IsAcquired);
    }

    [Fact]
    public void IsAcquired_WhenSessionEnded_ReturnsFalse()
    {
        Assert.False(LeaseAcquireResult.SessionEnded.IsAcquired);
    }

    #endregion
}
