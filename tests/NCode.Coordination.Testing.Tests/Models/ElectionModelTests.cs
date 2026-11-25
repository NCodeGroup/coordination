// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class ElectionModelTests
{
    private const string Alpha = "alpha";

    private const string Owner = "owner";

    private static Keyspace Space { get; } = new("test");

    private static CoordinationKey Role { get; } = new(Space, "election");

    private static TimeSpan Lifetime { get; } = TimeSpan.FromSeconds(30);

    private static ElectionModel CreateModel() => new();

    private static ElectionCall Lead { get; } = new(ElectionCall.Lead, Alpha, null, 0);

    private static ElectionCall Renew { get; } = new(ElectionCall.Renew, null, Owner, 5);

    private static ElectionCall Resign { get; } = new(ElectionCall.Resign, null, Owner, 5);

    private static ElectionCall Read { get; } = new(ElectionCall.Read, null, null, 0);

    private static ElectionState LedState { get; } = new(Owner, 5, 5, Alpha);

    private static ElectionResult Elected(long term, string owner, string value) =>
        ElectionResult.Elected(
            new Leadership(
                new Leader(Role, new FencingToken(term), HistoryValues.Encode(value)),
                new OwnerToken(owner),
                LeaseTerms.ForTimeToLive(new TimeToLive(Lifetime))
            )
        );

    private static ElectionView ViewOf(long term, string value) =>
        new(Role, new Leader(Role, new FencingToken(term), HistoryValues.Encode(value)));

    private static ElectionView NoLeader { get; } = new(Role, null);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenLeadIsElectedWhileVacant_HoldsTheLeadership()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, Elected(5, Owner, Alpha));

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenLeadTermIsNotAboveTheFloor_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, Elected(5, Owner, Alpha));
        var state = model.Initial with { Floor = 5 };

        var applied = model.TryApply(state, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLeadValueIsWrong_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, Elected(5, Owner, "beta"));

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLeadIsElectedWhileLed_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, Elected(6, "other", Alpha));

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLeadIsLedWhileLed_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, ElectionResult.Led);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenLeadIsLedWhileVacant_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, ElectionResult.Led);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLeadSessionEnded_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, ElectionResult.SessionEnded);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLeadReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Lead, 1L);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewByTheLeaderReturnsTrue_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, true);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenRenewByTheLeaderReturnsFalse_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, false);

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewIsStaleAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew with { Term = 4 }, false);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenRenewIsStaleAndReturnsTrue_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew with { Owner = "other" }, true);

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewComesWhileVacantAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, false);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenRenewReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Renew, 1L);

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenResignByTheLeaderReturnsTrue_VacatesTheRoleAndKeepsTheFloor()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Resign, true);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial with { Floor = 5 }, next);
    }

    [Fact]
    public void TryApply_WhenReadComesAfterAResign_NoLeaderIsAccepted()
    {
        var model = CreateModel();
        var resign = HistoryTesting.Returned(Resign, true);
        var read = HistoryTesting.Returned(Read, NoLeader);

        model.TryApply(LedState, resign, out var vacated);
        Assert.NotNull(vacated);
        var applied = model.TryApply(vacated, read, out var next);

        Assert.True(applied);
        Assert.Equal(vacated, next);
    }

    [Fact]
    public void TryApply_WhenReadComesAfterAResign_TheOldLeaderIsRejected()
    {
        var model = CreateModel();
        var resign = HistoryTesting.Returned(Resign, true);
        var read = HistoryTesting.Returned(Read, ViewOf(5, Alpha));

        model.TryApply(LedState, resign, out var vacated);
        Assert.NotNull(vacated);
        var applied = model.TryApply(vacated, read, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenResignIsStaleAndReturnsFalse_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Resign with { Owner = "other" }, false);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenResignIsStaleAndReturnsTrue_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Resign with { Term = 4 }, true);

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadSeesTheLeader_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, ViewOf(5, Alpha));

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenReadSeesNoLeaderWhileLed_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, NoLeader);

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadSeesNoLeaderWhileVacant_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, NoLeader);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenReadSeesALeaderWhileVacant_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, ViewOf(5, Alpha));

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadTermIsWrong_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, ViewOf(6, Alpha));

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadValueIsWrong_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, ViewOf(5, "beta"));

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenReadReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Read, 1L);

        var applied = model.TryApply(LedState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenLeadIsIndeterminateWhileVacant_HoldsAnUnknownLeader()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Lead);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(new ElectionState(ElectionModel.UnknownOwner, 0, 0, Alpha), next);
    }

    [Fact]
    public void TryApply_WhenLeadIsIndeterminateWhileLed_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Lead);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenAReadShowsTheTermOfAnIndeterminateLead_LearnsTheTerm()
    {
        var model = CreateModel();
        var indeterminate = HistoryTesting.Indeterminate(Lead);
        var read = HistoryTesting.Returned(Read, ViewOf(7, Alpha));

        model.TryApply(model.Initial, indeterminate, out var unknown);
        Assert.NotNull(unknown);
        var applied = model.TryApply(unknown, read, out var next);

        Assert.True(applied);
        Assert.Equal(new ElectionState(ElectionModel.UnknownOwner, 7, 7, Alpha), next);
    }

    [Fact]
    public void TryApply_WhenAReadShowsATermNotAboveTheFloorOfAnIndeterminateLead_Rejects()
    {
        var model = CreateModel();
        var indeterminate = HistoryTesting.Indeterminate(Lead);
        var read = HistoryTesting.Returned(Read, ViewOf(5, Alpha));
        var state = model.Initial with { Floor = 5 };

        model.TryApply(state, indeterminate, out var unknown);
        Assert.NotNull(unknown);
        var applied = model.TryApply(unknown, read, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRenewByTheLeaderIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Renew);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenResignByTheLeaderIsIndeterminate_VacatesTheRole()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Resign);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial with { Floor = 5 }, next);
    }

    [Fact]
    public void TryApply_WhenResignIsIndeterminateAndStale_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Resign with { Term = 4 });

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenReadIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Read);

        var applied = model.TryApply(LedState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(LedState, next);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(new ElectionCall("other", null, null, 0), null);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned("lead", Elected(5, Owner, Alpha));

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
