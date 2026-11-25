// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessExpiringTableTests
{
    private static TimeSpan OneSecond { get; } = TimeSpan.FromSeconds(1);

    private static TimeSpan TwoSeconds { get; } = TimeSpan.FromSeconds(2);

    private static TimeSpan ThreeSeconds { get; } = TimeSpan.FromSeconds(3);

    private static TimeSpan FourHundredDays { get; } = TimeSpan.FromDays(400);

    private static InProcessExpiry TwoSecondExpiry(TimeProvider time)
    {
        var timeToLive = new TimeToLive(TwoSeconds);
        return InProcessExpiry.Begin(time, timeToLive);
    }

    #region TryGet Tests

    [Fact]
    public void TryGet_WhenEntryIsLive_ReturnsTheValue()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("a", 7, expiry);

        time.Advance(OneSecond);

        var found = table.TryGet("a", out var value);
        Assert.True(found);
        Assert.Equal(7, value);
    }

    [Fact]
    public void TryGet_WhenEntryEnded_ReturnsFalse()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("a", 7, expiry);

        time.Advance(TwoSeconds);

        var found = table.TryGet("a", out _);
        Assert.False(found);
    }

    [Fact]
    public void TryGet_WhenEntryEnded_RemovesIt()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("a", 7, expiry);
        Assert.Equal(1, table.Count);

        time.Advance(TwoSeconds);

        table.TryGet("a", out _);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void TryGet_WhenEntryHasNoExpiry_NeverEnds()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("a", 7, null);

        time.Advance(FourHundredDays);

        var found = table.TryGet("a", out var value);
        Assert.True(found);
        Assert.Equal(7, value);
    }

    #endregion

    #region Update Tests

    [Fact]
    public void Update_WhenEntryIsLive_KeepsItsExpiry()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("a", 7, expiry);

        time.Advance(OneSecond);
        table.Update("a", 8);

        var foundBeforeEnd = table.TryGet("a", out var value);
        Assert.True(foundBeforeEnd);
        Assert.Equal(8, value);

        time.Advance(OneSecond);

        var foundAfterEnd = table.TryGet("a", out _);
        Assert.False(foundAfterEnd);
    }

    [Fact]
    public void Update_WhenKeyIsAbsent_ThrowsInvalidOperationException()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);

        Assert.Throws<InvalidOperationException>(() => table.Update("a", 8));
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_WhenEntryIsLive_ReturnsTrue()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("a", 7, expiry);

        var removed = table.Remove("a");

        Assert.True(removed);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Remove_WhenEntryEnded_ReturnsFalse()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("a", 7, expiry);

        time.Advance(TwoSeconds);

        var removed = table.Remove("a");
        Assert.False(removed);
        Assert.Equal(0, table.Count);
    }

    #endregion

    #region ListLive Tests

    [Fact]
    public void ListLive_WhenAnEntryEnded_LeavesItOut()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("short", 1, expiry);
        table.Set("forever", 2, null);

        time.Advance(TwoSeconds);

        var live = table.ListLive();
        var entry = Assert.Single(live);
        Assert.Equal("forever", entry.Key);
        Assert.Equal(2, entry.Value);
    }

    #endregion

    #region Set Tests

    [Fact]
    public void Set_WhenWritesReachTheSweepFloor_RemovesEndedEntries()
    {
        var time = InProcessTesting.CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        var expiry = TwoSecondExpiry(time);
        table.Set("old", 0, expiry);

        time.Advance(ThreeSeconds);
        for (var index = 0; index < InProcessExpiringTable<string, int>.SweepFloor; index++)
        {
            table.Set($"key-{index}", index, null);
        }

        Assert.Equal(InProcessExpiringTable<string, int>.SweepFloor, table.Count);
    }

    #endregion
}
