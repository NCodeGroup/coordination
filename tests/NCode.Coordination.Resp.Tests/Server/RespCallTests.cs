// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespCallTests
{
    #region Check Tests

    [Fact]
    public void Check_NullOptions_Returns()
    {
        RespCall.Check(null, CancellationToken.None);
    }

    [Fact]
    public void Check_EmptyOptions_Returns()
    {
        var options = new OperationOptions();

        RespCall.Check(options, CancellationToken.None);
    }

    [Fact]
    public void Check_UnknownExtension_ThrowsUnsupported()
    {
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

        var exception = Assert.Throws<CoordinationException>(() =>
            RespCall.Check(options, CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains("consul.lock-delay", exception.Message);
    }

    [Fact]
    public void Check_CanceledToken_ThrowsOperationCanceledException()
    {
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            RespCall.Check(options, cancellation.Token)
        );
    }

    #endregion

    #region ToMilliseconds Tests

    [Fact]
    public void ToMilliseconds_WholeMilliseconds_ReturnsThem()
    {
        var duration = TimeSpan.FromSeconds(2);
        var timeToLive = new TimeToLive(duration);

        var result = RespCall.ToMilliseconds(timeToLive);

        Assert.Equal(2000, result);
    }

    [Fact]
    public void ToMilliseconds_PartOfAMillisecond_RoundsUp()
    {
        var duration = TimeSpan.FromTicks(15_001);
        var timeToLive = new TimeToLive(duration);

        var result = RespCall.ToMilliseconds(timeToLive);

        Assert.Equal(2, result);
    }

    [Fact]
    public void ToMilliseconds_OneTick_RoundsUpToOne()
    {
        var duration = TimeSpan.FromTicks(1);
        var timeToLive = new TimeToLive(duration);

        var result = RespCall.ToMilliseconds(timeToLive);

        Assert.Equal(1, result);
    }

    [Fact]
    public void ToMilliseconds_NullTimeToLive_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RespCall.ToMilliseconds(null!));
    }

    #endregion
}
