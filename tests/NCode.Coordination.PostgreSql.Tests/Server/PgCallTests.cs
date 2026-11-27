// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgCallTests
{
    #region Check Tests

    [Fact]
    public void Check_NullOptions_Returns()
    {
        PgCall.Check(null, CancellationToken.None);
    }

    [Fact]
    public void Check_Extension_ThrowsUnsupportedNotApplied()
    {
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

        var exception = Assert.Throws<CoordinationException>(() =>
            PgCall.Check(options, CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
        Assert.Contains("consul.lock-delay", exception.Message);
    }

    [Fact]
    public void Check_CanceledToken_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => PgCall.Check(null, cancellation.Token));
    }

    #endregion

    #region ToMicroseconds Tests

    [Fact]
    public void ToMicroseconds_PartialMicrosecond_RoundsUp()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromTicks(1));

        Assert.Equal(1, PgCall.ToMicroseconds(timeToLive));
        Assert.Equal(2, PgCall.ToMicroseconds(TimeSpan.FromTicks(11)));
    }

    [Fact]
    public void ToMicroseconds_WholeMicroseconds_KeepsThem()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(2));

        Assert.Equal(2_000_000, PgCall.ToMicroseconds(timeToLive));
        Assert.Equal(20, PgCall.ToMicroseconds(TimeSpan.FromTicks(200)));
    }

    #endregion
}
