// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class KeepAliveResultTests
{
    #region Renewed Tests

    [Fact]
    public void Renewed_WhenTimeToLiveIsGiven_HasRenewedStatusAndPayload()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));

        var result = KeepAliveResult.Renewed(timeToLive);

        Assert.Equal(KeepAliveStatus.Renewed, result.Status);
        Assert.Same(timeToLive, result.TimeToLive);
    }

    [Fact]
    public void Renewed_WhenTimeToLiveIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => KeepAliveResult.Renewed(null!));

        Assert.Equal("timeToLive", exception.ParamName);
    }

    #endregion

    #region Ended Property Tests

    [Fact]
    public void Ended_Always_HasEndedStatusAndNoPayload()
    {
        var result = KeepAliveResult.Ended;

        Assert.Equal(KeepAliveStatus.Ended, result.Status);
        Assert.Null(result.TimeToLive);
    }

    [Fact]
    public void Ended_Always_IsOneSharedInstance()
    {
        Assert.Same(KeepAliveResult.Ended, KeepAliveResult.Ended);
    }

    #endregion

    #region IsRenewed Property Tests

    [Fact]
    public void IsRenewed_WhenRenewed_ReturnsTrue()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(30));
        var result = KeepAliveResult.Renewed(timeToLive);

        Assert.True(result.IsRenewed);
    }

    [Fact]
    public void IsRenewed_WhenEnded_ReturnsFalse()
    {
        Assert.False(KeepAliveResult.Ended.IsRenewed);
    }

    #endregion
}
