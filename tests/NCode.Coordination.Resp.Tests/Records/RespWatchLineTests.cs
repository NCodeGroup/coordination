// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Records.Tests;

public sealed class RespWatchLineTests
{
    #region TryParse Tests

    [Fact]
    public void TryParse_WhenTheEventHasLines_ReturnsEachKindAndName()
    {
        var parsed = RespWatchLine.TryParse("12\nw1:ta\nd0:tb\nmgroup", out var lines);

        Assert.True(parsed);
        Assert.Equal<RespWatchLine>([new('w', "1:ta"), new('d', "0:tb"), new('m', "group")], lines);
    }

    [Fact]
    public void TryParse_WhenTheEventIsTheRevisionAlone_ReturnsNoLines()
    {
        var parsed = RespWatchLine.TryParse("12", out var lines);

        Assert.True(parsed);
        Assert.Empty(lines);
    }

    [Fact]
    public void TryParse_WhenALineHasNoName_ReturnsFalse()
    {
        var parsed = RespWatchLine.TryParse("12\nw", out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParse_WhenALineIsEmpty_ReturnsFalse()
    {
        var parsed = RespWatchLine.TryParse("12\n\nw1:ta", out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParse_WhenTheEventEndsWithALineFeed_ReturnsFalse()
    {
        var parsed = RespWatchLine.TryParse("12\nw1:ta\n", out _);

        Assert.False(parsed);
    }

    #endregion
}
