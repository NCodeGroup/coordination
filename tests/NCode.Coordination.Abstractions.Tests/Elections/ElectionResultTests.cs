// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class ElectionResultTests
{
    private static Leadership Leadership()
    {
        var leader = new Leader(
            new CoordinationKey(new Keyspace("prod"), "workers"),
            new FencingToken(7),
            ReadOnlyMemory<byte>.Empty
        );
        var duration = TimeSpan.FromSeconds(30);
        var timeToLive = new TimeToLive(duration);
        var terms = LeaseTerms.ForTimeToLive(timeToLive);
        return new Leadership(leader, new OwnerToken("o-1"), terms);
    }

    #region Elected Tests

    [Fact]
    public void Elected_WhenLeadershipIsGiven_HasElectedStatusAndPayload()
    {
        var leadership = Leadership();

        var result = ElectionResult.Elected(leadership);

        Assert.Equal(ElectionStatus.Elected, result.Status);
        Assert.Same(leadership, result.Leadership);
    }

    [Fact]
    public void Elected_WhenLeadershipIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ElectionResult.Elected(null!));

        Assert.Equal("leadership", exception.ParamName);
    }

    #endregion

    #region Led Property Tests

    [Fact]
    public void Led_Always_HasLedStatusAndNoPayload()
    {
        var result = ElectionResult.Led;

        Assert.Equal(ElectionStatus.Led, result.Status);
        Assert.Null(result.Leadership);
    }

    [Fact]
    public void Led_Always_IsOneSharedInstance()
    {
        Assert.Same(ElectionResult.Led, ElectionResult.Led);
    }

    #endregion

    #region SessionEnded Property Tests

    [Fact]
    public void SessionEnded_Always_HasSessionEndedStatusAndNoPayload()
    {
        var result = ElectionResult.SessionEnded;

        Assert.Equal(ElectionStatus.SessionEnded, result.Status);
        Assert.Null(result.Leadership);
    }

    [Fact]
    public void SessionEnded_Always_IsOneSharedInstance()
    {
        Assert.Same(ElectionResult.SessionEnded, ElectionResult.SessionEnded);
    }

    #endregion

    #region IsElected Property Tests

    [Fact]
    public void IsElected_WhenElected_ReturnsTrue()
    {
        var leadership = Leadership();

        var result = ElectionResult.Elected(leadership);

        Assert.True(result.IsElected);
    }

    [Fact]
    public void IsElected_WhenLed_ReturnsFalse()
    {
        Assert.False(ElectionResult.Led.IsElected);
    }

    [Fact]
    public void IsElected_WhenSessionEnded_ReturnsFalse()
    {
        Assert.False(ElectionResult.SessionEnded.IsElected);
    }

    #endregion
}
