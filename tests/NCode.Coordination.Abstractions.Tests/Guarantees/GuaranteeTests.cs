// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class GuaranteeTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenNameIsValid_KeepsName()
    {
        const string name = "consistency.local";

        var guarantee = new Guarantee(name);

        Assert.Equal(name, guarantee.Name);
    }

    [Fact]
    public void Constructor_WhenNameIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Guarantee("Bad Name"));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenNameIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new Guarantee(null!));

        Assert.Equal("name", exception.ParamName);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenNamesMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new Guarantee("durable");
        var second = new Guarantee("durable");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_WhenNamesDiffer_ReturnsFalse()
    {
        var first = new Guarantee("durable");
        var second = new Guarantee("volatile");

        Assert.NotEqual(first, second);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_Always_ReturnsTheName()
    {
        var guarantee = new Guarantee("durable");

        Assert.Equal("durable", guarantee.ToString());
    }

    #endregion
}
