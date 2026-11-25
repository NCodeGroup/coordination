// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Validation.Tests;

public sealed class NamedValuesTests
{
    #region Check Tests

    [Fact]
    public void Check_WhenValuesAreEmpty_ReturnsEmpty()
    {
        var result = NamedValues.Check(ImmutableDictionary<string, string>.Empty, 2, 5, "values");

        Assert.Empty(result);
    }

    [Fact]
    public void Check_WhenValuesAreValid_ReturnsThemWithOrdinalKeys()
    {
        var values = ImmutableDictionary<string, string>.Empty.Add("a.b", "12345");

        var result = NamedValues.Check(values, 2, 5, "values");

        Assert.Equal("12345", result["a.b"]);
        Assert.Same(StringComparer.Ordinal, result.KeyComparer);
        Assert.Same(StringComparer.Ordinal, result.ValueComparer);
    }

    [Fact]
    public void Check_WhenValuesAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            NamedValues.Check(null!, 2, 5, "values")
        );

        Assert.Equal("values", exception.ParamName);
    }

    [Fact]
    public void Check_WhenThereAreTooManyEntries_Throws()
    {
        var values = ImmutableDictionary<string, string>
            .Empty.Add("a", "x")
            .Add("b", "x")
            .Add("c", "x");

        var exception = Assert.Throws<ArgumentException>(() =>
            NamedValues.Check(values, 2, 5, "values")
        );

        Assert.Equal("values", exception.ParamName);
        Assert.Contains("At most 2 entries", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_WhenAKeyIsNotAName_Throws()
    {
        var values = ImmutableDictionary<string, string>.Empty.Add("Bad Key", "x");

        var exception = Assert.Throws<ArgumentException>(() =>
            NamedValues.Check(values, 2, 5, "values")
        );

        Assert.Equal("values", exception.ParamName);
        Assert.Contains("is not a name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_WhenAValueIsNull_Throws()
    {
        var values = ImmutableDictionary<string, string>.Empty.Add("a", null!);

        var exception = Assert.Throws<ArgumentException>(() =>
            NamedValues.Check(values, 2, 5, "values")
        );

        Assert.Equal("values", exception.ParamName);
        Assert.Contains("is null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_WhenAValueIsTooLong_Throws()
    {
        var values = ImmutableDictionary<string, string>.Empty.Add("a", "123456");

        var exception = Assert.Throws<ArgumentException>(() =>
            NamedValues.Check(values, 2, 5, "values")
        );

        Assert.Equal("values", exception.ParamName);
        Assert.Contains("at most 5 characters", exception.Message, StringComparison.Ordinal);
    }

    #endregion
}
