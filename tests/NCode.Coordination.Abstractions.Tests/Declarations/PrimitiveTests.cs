// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class PrimitiveTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenNameIsValid_KeepsName()
    {
        const string name = "record-watch";

        var primitive = new Primitive(name);

        Assert.Equal(name, primitive.Name);
    }

    [Fact]
    public void Constructor_WhenNameIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Primitive("Bad Name"));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenNameIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new Primitive(null!));

        Assert.Equal("name", exception.ParamName);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenNamesMatch_ReturnsTrueWithEqualHashCodes()
    {
        var first = new Primitive("lease");
        var second = new Primitive("lease");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_WhenNamesDiffer_ReturnsFalse()
    {
        var first = new Primitive("lease");
        var second = new Primitive("session");

        Assert.NotEqual(first, second);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_Always_ReturnsTheName()
    {
        var primitive = new Primitive("lease");

        Assert.Equal("lease", primitive.ToString());
    }

    #endregion
}
