// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class CoordinationExceptionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenValid_KeepsEverything()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new CoordinationException(
            CoordinationErrorCodes.Unavailable,
            true,
            "down",
            inner
        );

        Assert.Equal("unavailable", exception.Code);
        Assert.True(exception.OutcomeUnknown);
        Assert.Equal("down", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void Constructor_WhenNoInnerException_LeavesItNull()
    {
        var exception = new CoordinationException(CoordinationErrorCodes.Unsupported, false, "no");

        Assert.False(exception.OutcomeUnknown);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Constructor_WhenCodeIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CoordinationException("Bad Code", false, "x")
        );

        Assert.Equal("code", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenCodeIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CoordinationException(null!, false, "x")
        );

        Assert.Equal("code", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenMessageIsWhitespace_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CoordinationException(CoordinationErrorCodes.AccessDenied, false, " ")
        );

        Assert.Equal("message", exception.ParamName);
    }

    #endregion

    #region Code Constants Tests

    [Fact]
    public void ErrorCodes_Always_HaveTheirNames()
    {
        Assert.Equal("limit-exceeded", CoordinationErrorCodes.LimitExceeded);
        Assert.Equal("access-denied", CoordinationErrorCodes.AccessDenied);
    }

    #endregion
}
