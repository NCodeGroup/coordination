// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;

namespace NCode.Coordination.PostgreSql.Keys.Tests;

public sealed class PgKeysTests
{
    #region EncodeSegment Tests

    [Fact]
    public void EncodeSegment_Text_EscapesReservedBytes()
    {
        var segment = KeySegment.FromString("a{b}/c%");

        var encoded = PgKeys.EncodeSegment(segment);

        Assert.Equal("ta%7Bb%7D%2Fc%25", encoded);
    }

    [Fact]
    public void EncodeSegment_NonAscii_EncodesUtf8()
    {
        var segment = KeySegment.FromString("\u00e9");

        var encoded = PgKeys.EncodeSegment(segment);

        Assert.Equal("t%C3%A9", encoded);
    }

    [Fact]
    public void EncodeSegment_Number_UsesInvariantDecimal()
    {
        var segment = KeySegment.FromInt64(-42);

        var encoded = PgKeys.EncodeSegment(segment);

        Assert.Equal("n-42", encoded);
    }

    [Fact]
    public void EncodeSegment_Uuid_UsesLowercaseHex()
    {
        var uuid = Guid.Parse("00112233-4455-6677-8899-AABBCCDDEEFF");
        var segment = KeySegment.FromGuid(uuid);

        var encoded = PgKeys.EncodeSegment(segment);

        Assert.Equal("u00112233445566778899aabbccddeeff", encoded);
    }

    #endregion

    #region TryDecodeSegment Tests

    [Theory]
    [InlineData("ta%7Bb%7D%2Fc%25")]
    [InlineData("t%C3%A9")]
    [InlineData("tAZaz09-._~")]
    [InlineData("n0")]
    [InlineData("n-42")]
    [InlineData("n9223372036854775807")]
    [InlineData("u00112233445566778899aabbccddeeff")]
    public void TryDecodeSegment_EveryKind_RoundTrips(string encoded)
    {
        var decoded = PgKeys.TryDecodeSegment(encoded, out var segment);

        Assert.True(decoded);
        Assert.NotNull(segment);
        var encodedSegment = PgKeys.EncodeSegment(segment);
        Assert.Equal(encoded, encodedSegment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x1")]
    [InlineData("t%7b")]
    [InlineData("t%7")]
    [InlineData("ta/b")]
    [InlineData("nabc")]
    [InlineData("uABC")]
    public void TryDecodeSegment_Malformed_ReturnsFalse(string encoded)
    {
        var decoded = PgKeys.TryDecodeSegment(encoded, out var segment);

        Assert.False(decoded);
        Assert.Null(segment);
    }

    #endregion

    #region KeyspaceName Tests

    [Fact]
    public void KeyspaceName_WithSegments_JoinsThem()
    {
        var keyspace = new Keyspace("prod", "a{b}/c", 42);

        var name = PgKeys.KeyspaceName(keyspace);

        Assert.Equal("prod/ta%7Bb%7D%2Fc/n42", name);
    }

    #endregion

    #region TryDecodeKey Tests

    [Fact]
    public void TryDecodeKey_KeyText_RoundTripsWithDepth()
    {
        var keyspace = new Keyspace("prod");
        var guid = Guid.NewGuid();
        var uuid = KeySegment.FromGuid(guid);
        var key = keyspace.CreateKey("a{b}/c %\u00e9", -42, uuid) with { PartitionDepth = 2 };
        var text = PgKeys.KeyText(key);

        var decoded = PgKeys.TryDecodeKey(keyspace, text, 2, out var result);

        Assert.True(decoded);
        Assert.Equal(key, result);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("xabc", 1)]
    [InlineData("tabc", 5)]
    public void TryDecodeKey_Malformed_ReturnsFalse(string text, int depth)
    {
        var keyspace = new Keyspace("prod");

        var decoded = PgKeys.TryDecodeKey(keyspace, text, depth, out var result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    #endregion

    #region Index Tests

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public void Index_EachSegment_AddsThirtyTwoBytes(int count)
    {
        var keyspace = new Keyspace("prod");
        var segments = Enumerable
            .Range(0, count)
            .Select(number => KeySegment.FromInt64(number))
            .ToArray();
        var key = new CoordinationKey(keyspace, segments);
        var expected = SHA256.HashSizeInBytes * count;

        var index = PgKeys.Index(key);

        Assert.Equal(expected, index.Length);
    }

    [Fact]
    public void Index_Prefix_IsABytePrefixOfAKeyUnderIt()
    {
        var keyspace = new Keyspace("prod");
        var prefix = keyspace.CreateKey("a", 1);
        var key = keyspace.CreateKey("a", 1, "b");

        var prefixIndex = PgKeys.Index(prefix);
        var keyIndex = PgKeys.Index(key);

        var startsWithPrefix = keyIndex.AsSpan().StartsWith(prefixIndex);
        Assert.True(startsWithPrefix);
    }

    [Fact]
    public void Index_DepthsDiffer_GivesTheSameIndex()
    {
        var keyspace = new Keyspace("prod");
        var shallow = keyspace.CreateKey("a", 1, "b") with { PartitionDepth = 1 };
        var deep = keyspace.CreateKey("a", 1, "b") with { PartitionDepth = 3 };

        var shallowIndex = PgKeys.Index(shallow);
        var deepIndex = PgKeys.Index(deep);

        Assert.Equal(shallowIndex, deepIndex);
    }

    [Fact]
    public void Index_KnownKey_IsTheDigestsOfItsEncodedSegments()
    {
        // SHA-256 of "ta%7Bb%7D", then SHA-256 of "n42", concatenated.
        const string expected =
            "c7d2ba7030d7ef4353a336cc10087e3783eba7a2c83c77bc81a01103602a8f21732fa6f2f761e0f3cb0559c0b5d5a3549c974b1786913882835b666d96fe1db9";
        var key = new Keyspace("prod").CreateKey("a{b}", 42);

        var first = PgKeys.EncodeSegment(key.Segments[0]);
        var second = PgKeys.EncodeSegment(key.Segments[1]);
        var index = PgKeys.Index(key);
        var hex = Convert.ToHexStringLower(index);

        Assert.Equal("ta%7Bb%7D", first);
        Assert.Equal("n42", second);
        Assert.Equal(expected, hex);
    }

    [Fact]
    public void Index_LongestKey_HasFiveHundredTwelveBytes()
    {
        var keyspace = new Keyspace("prod");
        var text = new string('\u00e9', KeySegment.MaxTextLength);
        var segments = Enumerable
            .Range(0, CoordinationKey.MaxSegments)
            .Select(_ => KeySegment.FromString(text))
            .ToArray();
        var key = new CoordinationKey(keyspace, segments);
        const int expected = SHA256.HashSizeInBytes * CoordinationKey.MaxSegments;

        var index = PgKeys.Index(key);

        Assert.Equal(expected, index.Length);
    }

    #endregion

    #region IndexAndText Tests

    [Fact]
    public void IndexAndText_Key_MatchesIndexAndKeyText()
    {
        var keyspace = new Keyspace("prod");
        var guid = Guid.NewGuid();
        var uuid = KeySegment.FromGuid(guid);
        var key = keyspace.CreateKey("a b/\u00fc", 42, uuid);
        var expectedIndex = PgKeys.Index(key);
        var expectedText = PgKeys.KeyText(key);

        var (index, text) = PgKeys.IndexAndText(key);

        Assert.Equal(expectedIndex, index);
        Assert.Equal(expectedText, text);
    }

    #endregion

    #region PageToken Tests

    [Fact]
    public void PageToken_LongestKey_FitsTheTokenLimit()
    {
        var index = new byte[SHA256.HashSizeInBytes * CoordinationKey.MaxSegments];

        var token = PgKeys.PageToken(index, 16);

        Assert.True(token.Length <= CoordinationLimits.MaxContinuationTokenLength);
    }

    [Fact]
    public void PageToken_Read_RoundTrips()
    {
        var keyspace = new Keyspace("prod");
        var key = keyspace.CreateKey("a", 1, "b");
        var index = PgKeys.Index(key);
        var prefixKey = keyspace.CreateKey("a");
        var prefix = PgKeys.Index(prefixKey);
        var token = PgKeys.PageToken(index, 2);

        var read = PgKeys.TryReadPageToken(token, prefix, out var result, out var depth);

        Assert.True(read);
        Assert.Equal(index, result);
        Assert.Equal(2, depth);
    }

    #endregion

    #region TryReadPageToken Tests

    [Fact]
    public void TryReadPageToken_OtherPrefix_ReturnsFalse()
    {
        var keyspace = new Keyspace("prod");
        var indexKey = keyspace.CreateKey("a", 1);
        var prefixKey = keyspace.CreateKey("b");
        var index = PgKeys.Index(indexKey);
        var prefix = PgKeys.Index(prefixKey);
        var token = PgKeys.PageToken(index, 1);

        var read = PgKeys.TryReadPageToken(token, prefix, out var result, out var depth);

        Assert.False(read);
        Assert.Null(result);
        Assert.Equal(0, depth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("AA")]
    [InlineData("AAAA")]
    public void TryReadPageToken_Garbage_ReturnsFalse(string token)
    {
        var read = PgKeys.TryReadPageToken(token, [], out var result, out var depth);

        Assert.False(read);
        Assert.Null(result);
        Assert.Equal(0, depth);
    }

    [Fact]
    public void TryReadPageToken_DepthBeyondSegments_ReturnsFalse()
    {
        var token = PgKeys.PageToken(new byte[SHA256.HashSizeInBytes], 2);

        var read = PgKeys.TryReadPageToken(token, [], out var result, out _);

        Assert.False(read);
        Assert.Null(result);
    }

    [Fact]
    public void TryReadPageToken_LongestToken_RoundTrips()
    {
        var keyspace = new Keyspace("prod");
        var segments = Enumerable
            .Range(0, CoordinationKey.MaxSegments)
            .Select(position => KeySegment.FromInt64(position))
            .ToArray();
        var key = new CoordinationKey(keyspace, segments);
        var index = PgKeys.Index(key);
        var token = PgKeys.PageToken(index, CoordinationKey.MaxSegments);

        var read = PgKeys.TryReadPageToken(token, [], out var result, out var depth);

        Assert.True(read);
        Assert.Equal(index, result);
        Assert.Equal(CoordinationKey.MaxSegments, depth);
    }

    #endregion

    #region PrefixEnd Tests

    [Fact]
    public void PrefixEnd_Bytes_IncrementsTheLastBelowFf()
    {
        var end = PgKeys.PrefixEnd([0x01, 0xFF]);

        Assert.Equal([0x02], end);
    }

    [Fact]
    public void PrefixEnd_AllFf_ReturnsNull()
    {
        var end = PgKeys.PrefixEnd([0xFF, 0xFF]);

        Assert.Null(end);
    }

    #endregion
}
