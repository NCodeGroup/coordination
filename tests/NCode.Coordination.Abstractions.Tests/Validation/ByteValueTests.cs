// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Validation.Tests;

public sealed class ByteValueTests
{
    #region Copy Tests

    [Fact]
    public void Copy_WhenValueIsWithinTheLimit_ReturnsAnEqualCopy()
    {
        const int maxLength = 16;
        byte[] source = [1, 2, 3];

        var result = ByteValue.Copy(source, maxLength, "value");

        source[0] = 9;
        var kept = result.ToArray();

        Assert.Equal<byte>([1, 2, 3], kept);
    }

    [Fact]
    public void Copy_WhenValueIsAtTheLimit_Accepts()
    {
        const int maxLength = 8;
        var source = new byte[maxLength];

        var result = ByteValue.Copy(source, maxLength, "value");

        Assert.Equal(maxLength, result.Length);
    }

    [Fact]
    public void Copy_WhenValueIsEmpty_ReturnsEmpty()
    {
        const int maxLength = 8;

        var result = ByteValue.Copy(ReadOnlyMemory<byte>.Empty, maxLength, "value");

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Copy_WhenValueIsLongerThanTheLimit_Throws()
    {
        const int maxLength = 8;
        const string paramName = "value";
        var source = new byte[maxLength + 1];

        var exception = Assert.Throws<ArgumentException>(() =>
            ByteValue.Copy(source, maxLength, paramName)
        );

        Assert.Equal(paramName, exception.ParamName);
    }

    #endregion
}
