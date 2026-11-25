// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class OccurrenceModelTests
{
    private static OccurrenceCall Record { get; } = new(OccurrenceCall.Record);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenFirstRecordIsRecorded_AppliesIt()
    {
        var model = new OccurrenceModel();
        var operation = HistoryTesting.Returned(Record, OccurrenceStatus.Recorded);

        var applied = model.TryApply(false, operation, out var next);

        Assert.True(applied);
        Assert.True(next);
    }

    [Fact]
    public void TryApply_WhenRecordedTwice_Rejects()
    {
        var model = new OccurrenceModel();
        var operation = HistoryTesting.Returned(Record, OccurrenceStatus.Recorded);

        var applied = model.TryApply(true, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenDuplicateFollowsARecord_AppliesIt()
    {
        var model = new OccurrenceModel();
        var operation = HistoryTesting.Returned(Record, OccurrenceStatus.Duplicate);

        var applied = model.TryApply(true, operation, out var next);

        Assert.True(applied);
        Assert.True(next);
    }

    [Fact]
    public void TryApply_WhenDuplicateComesFirst_Rejects()
    {
        var model = new OccurrenceModel();
        var operation = HistoryTesting.Returned(Record, OccurrenceStatus.Duplicate);

        var applied = model.TryApply(false, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenRecordIsIndeterminate_RecordsTheOccurrence()
    {
        var model = new OccurrenceModel();
        var operation = HistoryTesting.Indeterminate(Record);

        var applied = model.TryApply(false, operation, out var next);

        Assert.True(applied);
        Assert.True(next);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = new OccurrenceModel();
        var operation = HistoryTesting.Returned("record", OccurrenceStatus.Recorded);

        var applied = model.TryApply(false, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
