// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Validation.Tests;

public sealed class VocabularyNameTests
{
    #region Validate Tests

    [Theory]
    [InlineData("a")]
    [InlineData("consistency.local")]
    [InlineData("acme-x.y-2")]
    public void Validate_WhenValueIsAName_ReturnsValue(string value)
    {
        var result = VocabularyName.Validate(value, "name");

        Assert.Equal(value, result);
    }

    [Fact]
    public void Validate_WhenValueHasMaxLength_ReturnsValue()
    {
        var value = new string('a', VocabularyName.MaxLength);

        var result = VocabularyName.Validate(value, "name");

        Assert.Equal(value, result);
    }

    [Fact]
    public void Validate_WhenValueIsTooLong_Throws()
    {
        var value = new string('a', VocabularyName.MaxLength + 1);

        var exception = Assert.Throws<ArgumentException>(() =>
            VocabularyName.Validate(value, "name")
        );

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("at most 128", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a..b")]
    [InlineData("a.")]
    [InlineData(".a")]
    [InlineData("a-")]
    [InlineData("a b")]
    [InlineData("a_b")]
    [InlineData("a\n")]
    public void Validate_WhenValueIsNotAName_Throws(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            VocabularyName.Validate(value, "name")
        );

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("is not a name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenValueIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            VocabularyName.Validate(null!, "name")
        );

        Assert.Equal("name", exception.ParamName);
    }

    #endregion
}
