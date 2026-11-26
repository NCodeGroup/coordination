// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespCallTests
{
    #region Check Tests

    [Fact]
    public void Check_NoOptions_Returns()
    {
        RespCall.Check(null, CancellationToken.None);
        RespCall.Check(new OperationOptions(), CancellationToken.None);
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
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(2));

        var result = RespCall.ToMilliseconds(timeToLive);

        Assert.Equal(2000, result);
    }

    [Fact]
    public void ToMilliseconds_PartOfAMillisecond_RoundsUp()
    {
        Assert.Equal(2, RespCall.ToMilliseconds(new TimeToLive(TimeSpan.FromTicks(15_001))));
        Assert.Equal(1, RespCall.ToMilliseconds(new TimeToLive(TimeSpan.FromTicks(1))));
    }

    [Fact]
    public void ToMilliseconds_NullTimeToLive_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RespCall.ToMilliseconds(null!));
    }

    #endregion
}
