// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Keys.Tests;

public sealed class RespKeysTests
{
    #region KeyspaceTag Tests

    [Fact]
    public void KeyspaceTag_ClusterOnly_WrapsClusterInBraces()
    {
        var keyspace = new Keyspace("prod");

        var tag = RespKeys.KeyspaceTag(keyspace);

        Assert.Equal("{prod}", tag);
    }

    [Fact]
    public void KeyspaceTag_TextWithBracesAndSlash_EscapesThem()
    {
        var keyspace = new Keyspace("prod", "a{b}/c");

        var tag = RespKeys.KeyspaceTag(keyspace);

        Assert.Equal("{prod/ta%7Bb%7D%2Fc}", tag);
    }

    #endregion

    #region ForKey Tests

    [Fact]
    public void ForKey_TextAndNumberOne_Differ()
    {
        var keyspace = new Keyspace("prod");
        var textSegment = KeySegment.FromString("1");
        var numberSegment = KeySegment.FromInt64(1);
        var textKey = keyspace.CreateKey(textSegment);
        var numberKey = keyspace.CreateKey(numberSegment);

        var text = RespKeys.ForKey(textKey, "lock");
        var number = RespKeys.ForKey(numberKey, "lock");

        Assert.NotEqual(text, number);
    }

    [Fact]
    public void ForKey_SameSegmentsDifferentDepth_Differ()
    {
        var keyspace = new Keyspace("prod");
        var first = keyspace.CreateKey("a", "b") with { PartitionDepth = 1 };
        var second = keyspace.CreateKey("a", "b") with { PartitionDepth = 2 };

        var firstName = RespKeys.ForKey(first, "lock");
        var secondName = RespKeys.ForKey(second, "lock");

        Assert.NotEqual(firstName, secondName);
    }

    [Fact]
    public void ForKey_Uuid_UsesThirtyTwoHexDigits()
    {
        var uuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var keyspace = new Keyspace("prod");
        var uuidSegment = KeySegment.FromGuid(uuid);
        var key = keyspace.CreateKey(uuidSegment);

        var name = RespKeys.ForKey(key, "lock");

        Assert.Equal("{prod}:lock:0:u00112233445566778899aabbccddeeff", (string?)name);
    }

    [Fact]
    public void ForKey_NonAsciiText_PercentEncodesUtf8()
    {
        var keyspace = new Keyspace("prod");
        var key = keyspace.CreateKey("é");

        var name = RespKeys.ForKey(key, "lock");

        Assert.Equal("{prod}:lock:0:t%C3%A9", (string?)name);
    }

    [Fact]
    public void ForKey_AnyKey_FirstClosingBraceEndsTag()
    {
        var keyspace = new Keyspace("prod", "x}y");
        var numberSegment = KeySegment.FromInt64(7);
        var key = keyspace.CreateKey("a}b", numberSegment);

        var name = (string?)RespKeys.ForKey(key, "lock");

        Assert.NotNull(name);
        var end = name.IndexOf('}', StringComparison.Ordinal);
        var expected = RespKeys.KeyspaceTag(keyspace);
        var tag = name[..(end + 1)];
        Assert.Equal(expected, tag);
    }

    #endregion

    #region ForKeyspace Tests

    [Fact]
    public void ForKeyspace_InvalidKind_Throws()
    {
        var keyspace = new Keyspace("prod");

        var exception = Assert.Throws<ArgumentException>(() =>
            RespKeys.ForKeyspace(keyspace, "Bad Kind")
        );

        Assert.Equal("kind", exception.ParamName);
    }

    #endregion

    #region Escape Tests

    [Fact]
    public void Escape_LongNonAsciiText_EncodesEveryByte()
    {
        const string character = "\u00e9";
        var characters = Enumerable.Repeat(character, 400);
        var text = string.Concat(characters);
        var encodedCharacters = Enumerable.Repeat("%C3%A9", 400);
        var expected = string.Concat(encodedCharacters);

        var escaped = RespKeys.Escape(text);

        Assert.Equal(expected, escaped);
    }

    #endregion

    #region DecodeSegment Tests

    [Fact]
    public void DecodeSegment_EncodedText_ReturnsTheText()
    {
        var segment = KeySegment.FromString("a{b}/c %\u00e9");
        var encoded = RespKeys.EncodeSegment(segment);

        var decoded = RespKeys.DecodeSegment(encoded);

        Assert.Equal(segment, decoded);
    }

    [Fact]
    public void DecodeSegment_EncodedNumber_ReturnsTheNumber()
    {
        var segment = KeySegment.FromInt64(-42);
        var encoded = RespKeys.EncodeSegment(segment);

        var decoded = RespKeys.DecodeSegment(encoded);

        Assert.Equal(segment, decoded);
    }

    [Fact]
    public void DecodeSegment_EncodedUuid_ReturnsTheUuid()
    {
        var uuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var segment = KeySegment.FromGuid(uuid);
        var encoded = RespKeys.EncodeSegment(segment);

        var decoded = RespKeys.DecodeSegment(encoded);

        Assert.Equal(segment, decoded);
    }

    [Fact]
    public void DecodeSegment_EscapedBraces_ReturnsTheText()
    {
        const string encoded = "ta%7Bb%7D%2Fc";

        var decoded = RespKeys.DecodeSegment(encoded);

        Assert.Equal(KeySegment.FromString("a{b}/c"), decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x1")]
    [InlineData("t")]
    [InlineData("t%4")]
    [InlineData("t%g1")]
    [InlineData("t%41%")]
    [InlineData("ta b")]
    [InlineData("t%FF")]
    [InlineData("n")]
    [InlineData("n12a")]
    [InlineData("n+5")]
    [InlineData("n007")]
    [InlineData("u123")]
    [InlineData("u0123456789ABCDEF0123456789ABCDEF")]
    public void DecodeSegment_Malformed_ThrowsFormatException(string encoded)
    {
        Assert.Throws<FormatException>(() => RespKeys.DecodeSegment(encoded));
    }

    #endregion
}
