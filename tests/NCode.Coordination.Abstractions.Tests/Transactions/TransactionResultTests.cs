// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class TransactionResultTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsThem()
    {
        var revision = new Revision(12);

        var result = new TransactionResult(TransactionStatus.Succeeded, revision);

        Assert.Equal(TransactionStatus.Succeeded, result.Status);
        Assert.True(result.Succeeded);
        Assert.Same(revision, result.Revision);
    }

    [Fact]
    public void Constructor_WhenConditionsFailed_IsNotSucceeded()
    {
        var revision = new Revision(1);

        var result = new TransactionResult(TransactionStatus.Failed, revision);

        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Constructor_WhenTheSessionEnded_IsNotSucceeded()
    {
        var revision = new Revision(1);

        var result = new TransactionResult(TransactionStatus.SessionEnded, revision);

        Assert.Equal(TransactionStatus.SessionEnded, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Constructor_WhenStatusIsUndefined_Throws()
    {
        var revision = new Revision(1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TransactionResult((TransactionStatus)99, revision)
        );

        Assert.Equal("status", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenRevisionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TransactionResult(TransactionStatus.Succeeded, null!)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    #endregion
}
