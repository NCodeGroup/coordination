// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessExpiringTableTests
{
    private static TimeSpan TwoSeconds { get; } = TimeSpan.FromSeconds(2);

    private static FakeTimeProvider CreateTime() =>
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static InProcessExpiry TwoSecondExpiry(TimeProvider time) =>
        InProcessExpiry.Begin(time, new TimeToLive(TwoSeconds));

    #region TryGet Tests

    [Fact]
    public void TryGet_WhenEntryIsLive_ReturnsTheValue()
    {
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("a", 7, TwoSecondExpiry(time));

        time.Advance(TimeSpan.FromSeconds(1));

        var found = table.TryGet("a", out var value);
        Assert.True(found);
        Assert.Equal(7, value);
    }

    [Fact]
    public void TryGet_WhenEntryEnded_ReturnsFalseAndRemovesIt()
    {
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("a", 7, TwoSecondExpiry(time));
        Assert.Equal(1, table.Count);

        time.Advance(TwoSeconds);

        var found = table.TryGet("a", out _);
        Assert.False(found);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void TryGet_WhenEntryHasNoExpiry_NeverEnds()
    {
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("a", 7, null);

        time.Advance(TimeSpan.FromDays(400));

        var found = table.TryGet("a", out var value);
        Assert.True(found);
        Assert.Equal(7, value);
    }

    #endregion

    #region Update Tests

    [Fact]
    public void Update_WhenEntryIsLive_KeepsItsExpiry()
    {
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("a", 7, TwoSecondExpiry(time));

        time.Advance(TimeSpan.FromSeconds(1));
        table.Update("a", 8);

        var foundBeforeEnd = table.TryGet("a", out var value);
        Assert.True(foundBeforeEnd);
        Assert.Equal(8, value);

        time.Advance(TimeSpan.FromSeconds(1));

        var foundAfterEnd = table.TryGet("a", out _);
        Assert.False(foundAfterEnd);
    }

    [Fact]
    public void Update_WhenKeyIsAbsent_ThrowsInvalidOperationException()
    {
        var table = new InProcessExpiringTable<string, int>(CreateTime());

        Assert.Throws<InvalidOperationException>(() => table.Update("a", 8));
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_WhenEntryIsLive_ReturnsTrue()
    {
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("a", 7, TwoSecondExpiry(time));

        var removed = table.Remove("a");

        Assert.True(removed);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Remove_WhenEntryEnded_ReturnsFalse()
    {
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("a", 7, TwoSecondExpiry(time));

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
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("short", 1, TwoSecondExpiry(time));
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
        var time = CreateTime();
        var table = new InProcessExpiringTable<string, int>(time);
        table.Set("old", 0, TwoSecondExpiry(time));

        time.Advance(TimeSpan.FromSeconds(3));
        for (var index = 0; index < InProcessExpiringTable<string, int>.SweepFloor; index++)
        {
            table.Set($"key-{index}", index, null);
        }

        Assert.Equal(InProcessExpiringTable<string, int>.SweepFloor, table.Count);
    }

    #endregion
}
