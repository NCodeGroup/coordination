// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SingleFlightTermsTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheArgumentsAreValid_KeepsBothTimeToLives()
    {
        var valueTimeToLive = new TimeToLive(TimeSpan.FromMinutes(1));
        var fetchTimeToLive = new TimeToLive(TimeSpan.FromSeconds(5));

        var terms = new SingleFlightTerms(valueTimeToLive, fetchTimeToLive);

        Assert.Same(valueTimeToLive, terms.ValueTimeToLive);
        Assert.Same(fetchTimeToLive, terms.FetchTimeToLive);
    }

    [Fact]
    public void Constructor_WhenTheValueTimeToLiveIsNull_Throws()
    {
        var fetchTimeToLive = new TimeToLive(TimeSpan.FromSeconds(5));

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SingleFlightTerms(null!, fetchTimeToLive)
        );

        Assert.Equal("valueTimeToLive", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheFetchTimeToLiveIsNull_Throws()
    {
        var valueTimeToLive = new TimeToLive(TimeSpan.FromMinutes(1));

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SingleFlightTerms(valueTimeToLive, null!)
        );

        Assert.Equal("fetchTimeToLive", exception.ParamName);
    }

    #endregion
}
