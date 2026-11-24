// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Validation.Tests;

public sealed class ElementListTests
{
    #region Copy Tests

    [Fact]
    public void Copy_WhenItemsHoldElements_KeepsTheirOrder()
    {
        var result = ElementList.Copy(["b", "a", "c"], "items");

        Assert.Equal<string>(["b", "a", "c"], result);
    }

    [Fact]
    public void Copy_WhenItemsAreEmpty_ReturnsEmptyArray()
    {
        var result = ElementList.Copy(Array.Empty<string>(), "items");

        Assert.False(result.IsDefault);
        Assert.Empty(result);
    }

    [Fact]
    public void Copy_WhenItemsAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ElementList.Copy<string>(null!, "items")
        );

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Copy_WhenAnElementIsNull_Throws()
    {
        string[] items = ["a", null!];

        var exception = Assert.Throws<ArgumentException>(() => ElementList.Copy(items, "items"));

        Assert.Equal("items", exception.ParamName);
        Assert.Contains("null element", exception.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Check Tests

    [Fact]
    public void Check_WhenArrayIsValid_ReturnsIt()
    {
        var items = ImmutableArray.Create("a", "b");

        var result = ElementList.Check(items, "items");

        Assert.Equal<string>(items, result);
    }

    [Fact]
    public void Check_WhenArrayIsDefault_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ElementList.Check(default(ImmutableArray<string>), "items")
        );

        Assert.Equal("items", exception.ParamName);
        Assert.Contains("default ImmutableArray", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_WhenAnElementIsNull_Throws()
    {
        var items = ImmutableArray.Create("a", null!);

        var exception = Assert.Throws<ArgumentException>(() => ElementList.Check(items, "items"));

        Assert.Equal("items", exception.ParamName);
        Assert.Contains("null element", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
