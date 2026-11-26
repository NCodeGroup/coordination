// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class SingleFlightTermsTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheArgumentsAreValid_KeepsBothTimeToLives()
    {
        var valueLength = TimeSpan.FromMinutes(1);
        var fetchLength = TimeSpan.FromSeconds(5);
        var valueTimeToLive = new TimeToLive(valueLength);
        var fetchTimeToLive = new TimeToLive(fetchLength);

        var terms = new SingleFlightTerms(valueTimeToLive, fetchTimeToLive);

        Assert.Same(valueTimeToLive, terms.ValueTimeToLive);
        Assert.Same(fetchTimeToLive, terms.FetchTimeToLive);
    }

    [Fact]
    public void Constructor_WhenTheValueTimeToLiveIsNull_Throws()
    {
        var fetchLength = TimeSpan.FromSeconds(5);
        var fetchTimeToLive = new TimeToLive(fetchLength);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SingleFlightTerms(null!, fetchTimeToLive)
        );

        Assert.Equal("valueTimeToLive", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenTheFetchTimeToLiveIsNull_Throws()
    {
        var valueLength = TimeSpan.FromMinutes(1);
        var valueTimeToLive = new TimeToLive(valueLength);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            new SingleFlightTerms(valueTimeToLive, null!)
        );

        Assert.Equal("fetchTimeToLive", exception.ParamName);
    }

    #endregion
}
