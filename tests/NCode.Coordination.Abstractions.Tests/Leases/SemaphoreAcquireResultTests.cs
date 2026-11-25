// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SemaphoreAcquireResultTests
{
    private static SemaphoreGrant Grant()
    {
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);
        return new SemaphoreGrant(
            new CoordinationKey(new Keyspace("prod"), "pool"),
            2,
            new FencingToken(5),
            new OwnerToken("o-1"),
            terms
        );
    }

    #region Acquired Tests

    [Fact]
    public void Acquired_WhenGrantIsGiven_HasAcquiredStatusAndPayload()
    {
        var grant = Grant();

        var result = SemaphoreAcquireResult.Acquired(grant);

        Assert.Equal(SemaphoreAcquireStatus.Acquired, result.Status);
        Assert.Same(grant, result.Grant);
    }

    [Fact]
    public void Acquired_WhenGrantIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            SemaphoreAcquireResult.Acquired(null!)
        );

        Assert.Equal("grant", exception.ParamName);
    }

    #endregion

    #region Full Property Tests

    [Fact]
    public void Full_Always_HasFullStatusAndNoPayload()
    {
        var result = SemaphoreAcquireResult.Full;

        Assert.Equal(SemaphoreAcquireStatus.Full, result.Status);
        Assert.Null(result.Grant);
    }

    [Fact]
    public void Full_Always_IsOneSharedInstance()
    {
        Assert.Same(SemaphoreAcquireResult.Full, SemaphoreAcquireResult.Full);
    }

    #endregion

    #region CapacityConflict Property Tests

    [Fact]
    public void CapacityConflict_Always_HasCapacityConflictStatusAndNoPayload()
    {
        var result = SemaphoreAcquireResult.CapacityConflict;

        Assert.Equal(SemaphoreAcquireStatus.CapacityConflict, result.Status);
        Assert.Null(result.Grant);
    }

    [Fact]
    public void CapacityConflict_Always_IsOneSharedInstance()
    {
        Assert.Same(
            SemaphoreAcquireResult.CapacityConflict,
            SemaphoreAcquireResult.CapacityConflict
        );
    }

    #endregion

    #region SessionEnded Property Tests

    [Fact]
    public void SessionEnded_Always_HasSessionEndedStatusAndNoPayload()
    {
        var result = SemaphoreAcquireResult.SessionEnded;

        Assert.Equal(SemaphoreAcquireStatus.SessionEnded, result.Status);
        Assert.Null(result.Grant);
    }

    [Fact]
    public void SessionEnded_Always_IsOneSharedInstance()
    {
        Assert.Same(SemaphoreAcquireResult.SessionEnded, SemaphoreAcquireResult.SessionEnded);
    }

    #endregion

    #region IsAcquired Property Tests

    [Fact]
    public void IsAcquired_WhenAcquired_ReturnsTrue()
    {
        var grant = Grant();

        var result = SemaphoreAcquireResult.Acquired(grant);

        Assert.True(result.IsAcquired);
    }

    [Fact]
    public void IsAcquired_WhenFull_ReturnsFalse()
    {
        Assert.False(SemaphoreAcquireResult.Full.IsAcquired);
    }

    [Fact]
    public void IsAcquired_WhenCapacityConflict_ReturnsFalse()
    {
        Assert.False(SemaphoreAcquireResult.CapacityConflict.IsAcquired);
    }

    [Fact]
    public void IsAcquired_WhenSessionEnded_ReturnsFalse()
    {
        Assert.False(SemaphoreAcquireResult.SessionEnded.IsAcquired);
    }

    #endregion
}
