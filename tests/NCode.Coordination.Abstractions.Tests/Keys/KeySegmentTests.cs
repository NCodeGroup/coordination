// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class KeySegmentTests
{
    private static readonly Guid Sample = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    #region FromString Tests

    [Fact]
    public void FromString_WhenTextIsValid_SetsKindAndText()
    {
        const string text = "cell-1";

        var segment = KeySegment.FromString(text);

        Assert.Equal(KeySegmentKind.Text, segment.Kind);
        Assert.Equal(text, segment.Text);
        Assert.Null(segment.Number);
        Assert.Null(segment.Uuid);
    }

    [Fact]
    public void FromString_WhenTextIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => KeySegment.FromString(null!));
    }

    [Fact]
    public void FromString_WhenTextIsEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => KeySegment.FromString(string.Empty));
    }

    [Fact]
    public void FromString_WhenTextHasMaxLength_ReturnsSegment()
    {
        var text = new string('a', KeySegment.MaxTextLength);

        var segment = KeySegment.FromString(text);

        Assert.Equal(text, segment.Text);
    }

    [Fact]
    public void FromString_WhenTextIsTooLong_Throws()
    {
        var text = new string('a', KeySegment.MaxTextLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => KeySegment.FromString(text));

        Assert.Equal("text", exception.ParamName);
        Assert.Contains("at most 256", exception.Message, StringComparison.Ordinal);
        Assert.Contains("has 257", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromString_WhenTextHasUnpairedSurrogate_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => KeySegment.FromString("a\ud800b"));

        Assert.Equal("text", exception.ParamName);
        Assert.Contains("unpaired surrogate", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void FromString_WhenTextHasSurrogatePair_ReturnsSegment()
    {
        const string text = "\ud83d\ude00";

        var segment = KeySegment.FromString(text);

        Assert.Equal(text, segment.Text);
    }

    #endregion

    #region FromInt64 Tests

    [Fact]
    public void FromInt64_WhenCalled_SetsKindAndNumber()
    {
        var segment = KeySegment.FromInt64(42L);

        Assert.Equal(KeySegmentKind.Number, segment.Kind);
        Assert.Equal(42L, segment.Number);
        Assert.Null(segment.Text);
        Assert.Null(segment.Uuid);
    }

    #endregion

    #region FromGuid Tests

    [Fact]
    public void FromGuid_WhenCalled_SetsKindAndUuid()
    {
        var segment = KeySegment.FromGuid(Sample);

        Assert.Equal(KeySegmentKind.Uuid, segment.Kind);
        Assert.Equal(Sample, segment.Uuid);
        Assert.Null(segment.Text);
        Assert.Null(segment.Number);
    }

    #endregion

    #region Implicit Conversion Tests

    [Fact]
    public void ImplicitConversion_FromString_EqualsFactoryResult()
    {
        KeySegment segment = "cell-1";

        var expected = KeySegment.FromString("cell-1");

        Assert.Equal(expected, segment);
    }

    [Fact]
    public void ImplicitConversion_FromInt64_EqualsFactoryResult()
    {
        KeySegment segment = 7L;

        var expected = KeySegment.FromInt64(7L);

        Assert.Equal(expected, segment);
    }

    [Fact]
    public void ImplicitConversion_FromGuid_EqualsFactoryResult()
    {
        KeySegment segment = Sample;

        var expected = KeySegment.FromGuid(Sample);

        Assert.Equal(expected, segment);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_WhenTextAndNumberLookAlike_AreDifferent()
    {
        var text = KeySegment.FromString("1");
        var number = KeySegment.FromInt64(1L);

        Assert.NotEqual(text, number);
    }

    [Fact]
    public void Equals_WhenSegmentsHoldTheSameValue_AreEqualWithEqualHashCodes()
    {
        var firstText = KeySegment.FromString("a");
        var secondText = KeySegment.FromString("a");
        var firstNumber = KeySegment.FromInt64(5L);
        var secondNumber = KeySegment.FromInt64(5L);
        var firstUuid = KeySegment.FromGuid(Sample);
        var secondUuid = KeySegment.FromGuid(Sample);

        var firstTextHash = firstText.GetHashCode();
        var secondTextHash = secondText.GetHashCode();
        var firstNumberHash = firstNumber.GetHashCode();
        var secondNumberHash = secondNumber.GetHashCode();
        var firstUuidHash = firstUuid.GetHashCode();
        var secondUuidHash = secondUuid.GetHashCode();

        Assert.Equal(firstText, secondText);
        Assert.Equal(firstTextHash, secondTextHash);
        Assert.Equal(firstNumber, secondNumber);
        Assert.Equal(firstNumberHash, secondNumberHash);
        Assert.Equal(firstUuid, secondUuid);
        Assert.Equal(firstUuidHash, secondUuidHash);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_WhenKindIsText_ReturnsText()
    {
        var segment = KeySegment.FromString("cell-1");

        var text = segment.ToString();

        Assert.Equal("cell-1", text);
    }

    [Fact]
    public void ToString_WhenKindIsNumber_ReturnsInvariantNumber()
    {
        var segment = KeySegment.FromInt64(-42L);

        var text = segment.ToString();

        Assert.Equal("-42", text);
    }

    [Fact]
    public void ToString_WhenKindIsUuid_ReturnsHyphenatedUuid()
    {
        var segment = KeySegment.FromGuid(Sample);

        var text = segment.ToString();

        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", text);
    }

    #endregion
}
