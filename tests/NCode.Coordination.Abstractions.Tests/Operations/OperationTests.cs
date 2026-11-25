// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class OperationTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenNameIsValid_KeepsName()
    {
        const string name = "record.compare-and-swap";

        var operation = new Operation(name);

        Assert.Equal(name, operation.Name);
    }

    [Fact]
    public void Constructor_WhenNameIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Operation("Bad Name"));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenNameIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new Operation(null!));

        Assert.Equal("name", exception.ParamName);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenNamesMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new Operation("lease.renew");
        var second = new Operation("lease.renew");

        var firstHashCode = first.GetHashCode();
        var secondHashCode = second.GetHashCode();

        Assert.Equal(first, second);
        Assert.Equal(firstHashCode, secondHashCode);
    }

    [Fact]
    public void Equals_WhenNamesDiffer_ReturnsFalse()
    {
        var first = new Operation("lease.renew");
        var second = new Operation("lease.release");

        Assert.NotEqual(first, second);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_Always_ReturnsTheName()
    {
        var operation = new Operation("lease.renew");

        var text = operation.ToString();

        Assert.Equal("lease.renew", text);
    }

    #endregion
}
