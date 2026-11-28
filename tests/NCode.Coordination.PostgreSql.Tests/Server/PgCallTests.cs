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

        var one = PgCall.ToMicroseconds(timeToLive);
        var elevenTicks = TimeSpan.FromTicks(11);
        var two = PgCall.ToMicroseconds(elevenTicks);

        Assert.Equal(1, one);
        Assert.Equal(2, two);
    }

    [Fact]
    public void ToMicroseconds_WholeMicroseconds_KeepsThem()
    {
        var timeToLive = new TimeToLive(TimeSpan.FromSeconds(2));

        var twoSeconds = PgCall.ToMicroseconds(timeToLive);
        var twoHundredTicks = TimeSpan.FromTicks(200);
        var twenty = PgCall.ToMicroseconds(twoHundredTicks);

        Assert.Equal(2_000_000, twoSeconds);
        Assert.Equal(20, twenty);
    }

    #endregion

    #region DeadlineAfter Tests

    [Fact]
    public void DeadlineAfter_PartialMicrosecond_RoundsUp()
    {
        var now = new DateTime(2026, 11, 27, 20, 0, 0, DateTimeKind.Utc);
        var timeToLive = new TimeToLive(TimeSpan.FromTicks(11));

        var deadline = PgCall.DeadlineAfter(now, timeToLive);

        var expected = now.AddTicks(20);
        Assert.Equal(expected, deadline);
    }

    #endregion

    #region FromMicroseconds Tests

    [Fact]
    public void FromMicroseconds_Count_IsTenTicksEach()
    {
        var duration = PgCall.FromMicroseconds(3);

        var expected = TimeSpan.FromTicks(30);
        Assert.Equal(expected, duration);
    }

    #endregion
}
