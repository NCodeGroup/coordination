// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecordWriteResultTests
{
    #region Written Tests

    [Fact]
    public void Written_WhenRevisionIsGiven_HasWrittenStatusAndPayload()
    {
        var revision = new Revision(9);

        var result = RecordWriteResult.Written(revision);

        Assert.Equal(RecordWriteStatus.Written, result.Status);
        Assert.Same(revision, result.Revision);
    }

    [Fact]
    public void Written_WhenRevisionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            RecordWriteResult.Written(null!)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    #endregion

    #region Conflict Tests

    [Fact]
    public void Conflict_Always_HasConflictStatusAndNoPayload()
    {
        var result = RecordWriteResult.Conflict;

        Assert.Equal(RecordWriteStatus.Conflict, result.Status);
        Assert.Null(result.Revision);
    }

    [Fact]
    public void Conflict_Always_IsOneSharedInstance()
    {
        Assert.Same(RecordWriteResult.Conflict, RecordWriteResult.Conflict);
    }

    #endregion

    #region NotFound Tests

    [Fact]
    public void NotFound_Always_HasNotFoundStatusAndNoPayload()
    {
        var result = RecordWriteResult.NotFound;

        Assert.Equal(RecordWriteStatus.NotFound, result.Status);
        Assert.Null(result.Revision);
    }

    [Fact]
    public void NotFound_Always_IsOneSharedInstance()
    {
        Assert.Same(RecordWriteResult.NotFound, RecordWriteResult.NotFound);
    }

    #endregion

    #region SessionEnded Tests

    [Fact]
    public void SessionEnded_Always_HasSessionEndedStatusAndNoPayload()
    {
        var result = RecordWriteResult.SessionEnded;

        Assert.Equal(RecordWriteStatus.SessionEnded, result.Status);
        Assert.Null(result.Revision);
    }

    [Fact]
    public void SessionEnded_Always_IsOneSharedInstance()
    {
        Assert.Same(RecordWriteResult.SessionEnded, RecordWriteResult.SessionEnded);
    }

    #endregion

    #region IsWritten Property Tests

    [Fact]
    public void IsWritten_WhenWritten_ReturnsTrue()
    {
        Assert.True(RecordWriteResult.Written(new Revision(9)).IsWritten);
    }

    [Fact]
    public void IsWritten_WhenConflict_ReturnsFalse()
    {
        Assert.False(RecordWriteResult.Conflict.IsWritten);
    }

    [Fact]
    public void IsWritten_WhenNotFound_ReturnsFalse()
    {
        Assert.False(RecordWriteResult.NotFound.IsWritten);
    }

    [Fact]
    public void IsWritten_WhenSessionEnded_ReturnsFalse()
    {
        Assert.False(RecordWriteResult.SessionEnded.IsWritten);
    }

    #endregion
}
