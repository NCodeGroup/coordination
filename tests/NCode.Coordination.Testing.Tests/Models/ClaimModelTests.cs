// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class ClaimModelTests
{
    private static ClaimCall Claim { get; } = new(ClaimCall.Claim);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenFirstClaimIsClaimed_AppliesIt()
    {
        var model = new ClaimModel();
        var operation = HistoryTesting.Returned(Claim, ClaimStatus.Claimed);

        var applied = model.TryApply(false, operation, out var next);

        Assert.True(applied);
        Assert.True(next);
    }

    [Fact]
    public void TryApply_WhenClaimedTwice_Rejects()
    {
        var model = new ClaimModel();
        var operation = HistoryTesting.Returned(Claim, ClaimStatus.Claimed);

        var applied = model.TryApply(true, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenTakenFollowsAClaim_AppliesIt()
    {
        var model = new ClaimModel();
        var operation = HistoryTesting.Returned(Claim, ClaimStatus.Taken);

        var applied = model.TryApply(true, operation, out var next);

        Assert.True(applied);
        Assert.True(next);
    }

    [Fact]
    public void TryApply_WhenTakenComesFirst_Rejects()
    {
        var model = new ClaimModel();
        var operation = HistoryTesting.Returned(Claim, ClaimStatus.Taken);

        var applied = model.TryApply(false, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenClaimIsIndeterminate_ClaimsTheName()
    {
        var model = new ClaimModel();
        var operation = HistoryTesting.Indeterminate(Claim);

        var applied = model.TryApply(false, operation, out var next);

        Assert.True(applied);
        Assert.True(next);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = new ClaimModel();
        var operation = HistoryTesting.Returned("claim", ClaimStatus.Claimed);

        var applied = model.TryApply(false, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
