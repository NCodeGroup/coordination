// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class BarrierTermsTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheArgumentsAreValid_KeepsTheValues()
    {
        var lifetime = RecordLifetime.Persistent;

        var terms = new BarrierTerms(3, lifetime);

        Assert.Equal(3, terms.Parties);
        Assert.Same(lifetime, terms.Lifetime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CoordinationLimits.MaxPageSize + 1)]
    public void Constructor_WhenThePartiesAreOutsideTheBounds_Throws(int parties)
    {
        var lifetime = RecordLifetime.Persistent;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BarrierTerms(parties, lifetime)
        );

        Assert.Equal("parties", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(CoordinationLimits.MaxPageSize)]
    public void Constructor_WhenThePartiesAreAtTheBounds_KeepsThem(int parties)
    {
        var lifetime = RecordLifetime.Persistent;

        var terms = new BarrierTerms(parties, lifetime);

        Assert.Equal(parties, terms.Parties);
    }

    [Fact]
    public void Constructor_WhenTheLifetimeIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new BarrierTerms(3, null!));

        Assert.Equal("lifetime", exception.ParamName);
    }

    #endregion
}
