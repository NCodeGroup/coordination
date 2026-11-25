// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecordPageRequestTests
{
    #region Constructor Tests

    [Theory]
    [InlineData(1)]
    [InlineData(1_000)]
    public void Constructor_WhenLimitIsInRange_KeepsIt(int limit)
    {
        var request = new RecordPageRequest(limit);

        Assert.Equal(limit, request.Limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_001)]
    public void Constructor_WhenLimitIsOutOfRange_Throws(int limit)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RecordPageRequest(limit)
        );

        Assert.Equal("limit", exception.ParamName);
    }

    #endregion

    #region ContinuationToken Property Tests

    [Fact]
    public void ContinuationToken_WhenNotSet_IsNull()
    {
        var request = new RecordPageRequest(10);

        Assert.Null(request.ContinuationToken);
    }

    [Fact]
    public void ContinuationToken_WhenValid_KeepsIt()
    {
        var request = new RecordPageRequest(10) { ContinuationToken = "abc123" };

        Assert.Equal("abc123", request.ContinuationToken);
    }

    [Fact]
    public void ContinuationToken_WhenNullIsSet_IsNull()
    {
        var request = new RecordPageRequest(10) { ContinuationToken = null };

        Assert.Null(request.ContinuationToken);
    }

    [Fact]
    public void ContinuationToken_WhenInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new RecordPageRequest(10) { ContinuationToken = "a b" }
        );

        Assert.Equal("ContinuationToken", exception.ParamName);
    }

    #endregion
}
