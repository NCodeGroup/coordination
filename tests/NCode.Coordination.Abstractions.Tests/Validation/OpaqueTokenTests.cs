// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Validation.Tests;

public sealed class OpaqueTokenTests
{
    #region Validate Tests

    [Theory]
    [InlineData("a")]
    [InlineData("abc-123_XYZ")]
    [InlineData("12345678")]
    public void Validate_WhenValueIsAToken_ReturnsValue(string value)
    {
        const int maxLength = 16;

        var result = OpaqueToken.Validate(value, maxLength, "name");

        Assert.Equal(value, result);
    }

    [Fact]
    public void Validate_WhenValueHasMaxLength_ReturnsValue()
    {
        var value = new string('a', 8);

        var result = OpaqueToken.Validate(value, 8, "name");

        Assert.Equal(value, result);
    }

    [Fact]
    public void Validate_WhenValueIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            OpaqueToken.Validate(null!, 8, "name")
        );

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Validate_WhenValueIsEmpty_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => OpaqueToken.Validate("", 8, "name"));

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("1 to 8 characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WhenValueIsTooLong_Throws()
    {
        var value = new string('a', 9);

        var exception = Assert.Throws<ArgumentException>(() =>
            OpaqueToken.Validate(value, 8, "name")
        );

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("1 to 8 characters", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a\tb")]
    [InlineData("caf\u00e9")]
    [InlineData("cafe\u0301")]
    public void Validate_WhenValueHasACharacterOutsideVisibleAscii_Throws(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            OpaqueToken.Validate(value, 8, "name")
        );

        Assert.Equal("name", exception.ParamName);
        Assert.Contains("visible ASCII", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    #endregion

    #region ValidateOptional Tests

    [Fact]
    public void ValidateOptional_WhenValueIsNull_ReturnsNull()
    {
        const int maxLength = 8;

        var result = OpaqueToken.ValidateOptional(null, maxLength, "name");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateOptional_WhenValueIsAToken_ReturnsIt()
    {
        const string value = "abc-123";
        const int maxLength = 8;

        var result = OpaqueToken.ValidateOptional(value, maxLength, "name");

        Assert.Equal(value, result);
    }

    [Fact]
    public void ValidateOptional_WhenValueIsNotAToken_Throws()
    {
        const string value = "a b";
        const int maxLength = 8;

        var exception = Assert.Throws<ArgumentException>(() =>
            OpaqueToken.ValidateOptional(value, maxLength, "name")
        );

        Assert.Equal("name", exception.ParamName);
    }

    #endregion
}
