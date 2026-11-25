// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class SetModelTests
{
    private static ImmutableArray<KeySegment> Members { get; } = ["m0", "m1", "m2"];

    private static SetModel CreateModel() => new(Members);

    private static SetCall Add(int member) => new(SetCall.Add, member);

    private static SetCall Remove(int member) => new(SetCall.Remove, member);

    private static SetCall List { get; } = new(SetCall.List, 0);

    private static SetCall Clear { get; } = new(SetCall.Clear, 0);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenAddReturnsTrueForAnAbsentMember_AppliesIt()
    {
        var model = CreateModel();
        var call = Add(1);
        var operation = HistoryTesting.Returned(call, true);

        var applied = model.TryApply(0b001, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b011, next);
    }

    [Fact]
    public void TryApply_WhenAddReturnsFalseForAPresentMember_KeepsTheState()
    {
        var model = CreateModel();
        var call = Add(0);
        var operation = HistoryTesting.Returned(call, false);

        var applied = model.TryApply(0b001, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b001, next);
    }

    [Fact]
    public void TryApply_WhenAddReturnsTrueForAPresentMember_Rejects()
    {
        var model = CreateModel();
        var call = Add(0);
        var operation = HistoryTesting.Returned(call, true);

        var applied = model.TryApply(0b001, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAddReturnsFalseForAnAbsentMember_Rejects()
    {
        var model = CreateModel();
        var call = Add(1);
        var operation = HistoryTesting.Returned(call, false);

        var applied = model.TryApply(0b001, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRemoveReturnsTrueForAPresentMember_AppliesIt()
    {
        var model = CreateModel();
        var call = Remove(0);
        var operation = HistoryTesting.Returned(call, true);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b010, next);
    }

    [Fact]
    public void TryApply_WhenRemoveReturnsFalseForAnAbsentMember_KeepsTheState()
    {
        var model = CreateModel();
        var call = Remove(2);
        var operation = HistoryTesting.Returned(call, false);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b011, next);
    }

    [Fact]
    public void TryApply_WhenRemoveReturnsTrueForAnAbsentMember_Rejects()
    {
        var model = CreateModel();
        var call = Remove(2);
        var operation = HistoryTesting.Returned(call, true);

        var applied = model.TryApply(0b011, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenAddReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var call = Add(1);
        var operation = HistoryTesting.Returned(call, 1L);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenListReturnsTheMembers_KeepsTheState()
    {
        var model = CreateModel();
        ImmutableArray<KeySegment> listed = ["m2", "m0"];
        var operation = HistoryTesting.Returned(List, listed);

        var applied = model.TryApply(0b101, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b101, next);
    }

    [Fact]
    public void TryApply_WhenListReturnsNothingForAnEmptySet_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(List, ImmutableArray<KeySegment>.Empty);

        var applied = model.TryApply(0, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0, next);
    }

    [Fact]
    public void TryApply_WhenListMissesAMember_Rejects()
    {
        var model = CreateModel();
        ImmutableArray<KeySegment> listed = ["m0"];
        var operation = HistoryTesting.Returned(List, listed);

        var applied = model.TryApply(0b101, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenListReturnsAnUnknownMember_Rejects()
    {
        var model = CreateModel();
        ImmutableArray<KeySegment> listed = ["m0", "other"];
        var operation = HistoryTesting.Returned(List, listed);

        var applied = model.TryApply(0b001, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenListRepeatsAMember_Rejects()
    {
        var model = CreateModel();
        ImmutableArray<KeySegment> listed = ["m0", "m0"];
        var operation = HistoryTesting.Returned(List, listed);

        var applied = model.TryApply(0b001, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenListReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(List, 1L);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenClearReturnsAnything_EmptiesTheSet()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Clear, null);

        var applied = model.TryApply(0b111, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0, next);
    }

    [Fact]
    public void TryApply_WhenAddIsIndeterminate_AddsTheMember()
    {
        var model = CreateModel();
        var call = Add(2);
        var operation = HistoryTesting.Indeterminate(call);

        var applied = model.TryApply(0b001, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b101, next);
    }

    [Fact]
    public void TryApply_WhenRemoveIsIndeterminate_RemovesTheMember()
    {
        var model = CreateModel();
        var call = Remove(0);
        var operation = HistoryTesting.Indeterminate(call);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b010, next);
    }

    [Fact]
    public void TryApply_WhenListIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(List);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b011, next);
    }

    [Fact]
    public void TryApply_WhenClearIsIndeterminate_EmptiesTheSet()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Clear);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0, next);
    }

    [Fact]
    public void TryApply_WhenMemberIsUnknown_Rejects()
    {
        var model = CreateModel();
        var call = Add(3);
        var operation = HistoryTesting.Returned(call, true);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned("add", true);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
