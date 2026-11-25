// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class MembershipModelTests
{
    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Group { get; } = new(Space, "group");

    private static ImmutableArray<SessionId> Sessions { get; } =
    [new(Space, "s0"), new(Space, "s1"), new(Space, "s2")];

    private static MembershipModel CreateModel() => new(Sessions);

    private static MembershipCall Join(int client) => new(MembershipCall.Join, client);

    private static MembershipCall Leave(int client) => new(MembershipCall.Leave, client);

    private static MembershipCall Read { get; } = new(MembershipCall.Read, 0);

    private static MembershipView ViewOf(params SessionId[] members) =>
        new(
            Group,
            new Revision(1),
            members.Select(session => new Member(session, "node", "address"))
        );

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenJoinReturnsJoined_AppliesIt()
    {
        var model = CreateModel();
        var call = Join(1);
        var operation = HistoryTesting.Returned(call, MembershipJoinStatus.Joined);

        var applied = model.TryApply(0b001, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b011, next);
    }

    [Fact]
    public void TryApply_WhenJoinReturnsNodeIdTaken_Rejects()
    {
        var model = CreateModel();
        var call = Join(1);
        var operation = HistoryTesting.Returned(call, MembershipJoinStatus.NodeIdTaken);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLeaveReturns_RemovesTheMember()
    {
        var model = CreateModel();
        var call = Leave(0);
        var operation = HistoryTesting.Returned(call, true);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b010, next);
    }

    [Fact]
    public void TryApply_WhenLeaveNamesAnAbsentMember_KeepsTheState()
    {
        var model = CreateModel();
        var call = Leave(2);
        var operation = HistoryTesting.Returned(call, false);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b011, next);
    }

    [Fact]
    public void TryApply_WhenReadReturnsTheMembers_KeepsTheState()
    {
        var model = CreateModel();
        var view = ViewOf(Sessions[2], Sessions[0]);
        var operation = HistoryTesting.Returned(Read, view);

        var applied = model.TryApply(0b101, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b101, next);
    }

    [Fact]
    public void TryApply_WhenReadReturnsNoMembersForAnEmptyGroup_KeepsTheState()
    {
        var model = CreateModel();
        var view = ViewOf();
        var operation = HistoryTesting.Returned(Read, view);

        var applied = model.TryApply(0, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0, next);
    }

    [Fact]
    public void TryApply_WhenReadMissesAMember_Rejects()
    {
        var model = CreateModel();
        var view = ViewOf(Sessions[0]);
        var operation = HistoryTesting.Returned(Read, view);

        var applied = model.TryApply(0b101, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsAnUnknownSession_Rejects()
    {
        var model = CreateModel();
        var view = ViewOf(Sessions[0], new SessionId(Space, "other"));
        var operation = HistoryTesting.Returned(Read, view);

        var applied = model.TryApply(0b001, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadRepeatsASession_Rejects()
    {
        var model = CreateModel();
        var view = ViewOf(Sessions[0], Sessions[0]);
        var operation = HistoryTesting.Returned(Read, view);

        var applied = model.TryApply(0b001, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, 1L);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenJoinIsIndeterminate_AddsTheMember()
    {
        var model = CreateModel();
        var call = Join(2);
        var operation = HistoryTesting.Indeterminate(call);

        var applied = model.TryApply(0b001, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b101, next);
    }

    [Fact]
    public void TryApply_WhenLeaveIsIndeterminate_RemovesTheMember()
    {
        var model = CreateModel();
        var call = Leave(0);
        var operation = HistoryTesting.Indeterminate(call);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b010, next);
    }

    [Fact]
    public void TryApply_WhenReadIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Read);

        var applied = model.TryApply(0b011, operation, out var next);

        Assert.True(applied);
        Assert.Equal(0b011, next);
    }

    [Fact]
    public void TryApply_WhenClientIsUnknown_Rejects()
    {
        var model = CreateModel();
        var call = Join(3);
        var operation = HistoryTesting.Returned(call, MembershipJoinStatus.Joined);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned("join", MembershipJoinStatus.Joined);

        var applied = model.TryApply(0, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
