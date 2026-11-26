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

    #region Name Tests

    [Fact]
    public void Name_Key_IsTheDepthAndTheEncodedSegments()
    {
        var key = new Keyspace("prod").CreateKey("cfg", 7) with { PartitionDepth = 1 };

        var name = RespKeys.Name(key);

        Assert.Equal("1:tcfg/n7", name);
    }

    #endregion

    #region DecodeName Tests

    [Fact]
    public void DecodeName_Name_ReturnsTheKey()
    {
        var keyspace = new Keyspace("prod");
        var id = Guid.NewGuid();
        var uuid = KeySegment.FromGuid(id);
        var key = keyspace.CreateKey("a{b}/c %\u00e9", -42, uuid) with { PartitionDepth = 2 };
        var name = RespKeys.Name(key);

        var decoded = RespKeys.DecodeName(keyspace, name);

        Assert.Equal(key, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ta")]
    [InlineData(":ta")]
    [InlineData("x:ta")]
    [InlineData("01:ta")]
    [InlineData("-1:ta")]
    [InlineData("0:")]
    [InlineData("0:ta/")]
    [InlineData("2:ta")]
    [InlineData("0:x1")]
    public void DecodeName_Malformed_ThrowsFormatException(string name)
    {
        var keyspace = new Keyspace("prod");

        Assert.Throws<FormatException>(() => RespKeys.DecodeName(keyspace, name));
    }

    #endregion

    #region MemberPrefix Tests

    [Fact]
    public void MemberPrefix_Prefix_StartsTheMemberOfAKeyUnderIt()
    {
        var keyspace = new Keyspace("prod");
        var prefix = keyspace.CreateKey("a");
        var key = keyspace.CreateKey("a", "b") with { PartitionDepth = 1 };

        var prefixText = RespKeys.MemberPrefix(prefix);
        var member = RespKeys.Member(key);

        Assert.StartsWith(prefixText, member, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPrefix_OtherSegment_DoesNotStartTheMember()
    {
        var keyspace = new Keyspace("prod");
        var prefix = keyspace.CreateKey("a");
        var key = keyspace.CreateKey("ab");

        var prefixText = RespKeys.MemberPrefix(prefix);
        var member = RespKeys.Member(key);
        var starts = member.StartsWith(prefixText, StringComparison.Ordinal);

        Assert.False(starts);
    }

    #endregion

    #region Member Tests

    [Fact]
    public void Member_Key_IsTheDigestOfEachSegmentAndTheDepth()
    {
        var key = new Keyspace("prod").CreateKey("cfg", 7);

        var member = RespKeys.Member(key);

        // From sha256sum and base64 in valkey/valkey:9.2.0: the SHA-256 of "tcfg" and of "n7", in unpadded Base64Url.
        Assert.Equal(
            "VDlgg0fsjSWAj6iEqf_4sjULd91lsdHkOhNtG4yXTB8/b166Ixm9dYRxH8IcLrXUu4YzJ7t6Wux-qS7_2_kXzY8/0",
            member
        );
    }

    [Fact]
    public void Member_LongestKey_FitsAContinuationToken()
    {
        var text = new string('\u00e9', 256);
        var segment = KeySegment.FromString(text);
        var segments = Enumerable.Repeat(segment, 16);
        var key = new Keyspace("prod").CreateKey(segments) with { PartitionDepth = 16 };

        var member = RespKeys.Member(key);

        Assert.InRange(member.Length, 1, CoordinationLimits.MaxContinuationTokenLength);
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
        var expected = KeySegment.FromString("a{b}/c");

        var decoded = RespKeys.DecodeSegment(encoded);

        Assert.Equal(expected, decoded);
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
    [InlineData("t%41")]
    [InlineData("ta%2Fb%41")]
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
