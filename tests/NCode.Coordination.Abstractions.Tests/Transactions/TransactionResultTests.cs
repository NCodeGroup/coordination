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

        var result = new TransactionResult(true, revision);

        Assert.True(result.Succeeded);
        Assert.Same(revision, result.Revision);
    }

    [Fact]
    public void Constructor_WhenConditionsFailed_KeepsFalse()
    {
        var result = new TransactionResult(false, new Revision(1));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Constructor_WhenRevisionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TransactionResult(true, null!)
        );

        Assert.Equal("revision", exception.ParamName);
    }

    #endregion
}
