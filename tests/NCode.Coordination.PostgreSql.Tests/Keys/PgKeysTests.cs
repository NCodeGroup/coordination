// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
        Assert.Equal(encoded, PgKeys.EncodeSegment(segment));
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
        var uuid = KeySegment.FromGuid(Guid.NewGuid());
        var key = keyspace.CreateKey("a{b}/c %\u00e9", -42, uuid) with { PartitionDepth = 2 };
        var text = PgKeys.KeyText(key);

        var decoded = PgKeys.TryDecodeKey(keyspace, text, 2, out var result);

        Assert.True(decoded);
        Assert.Equal(key, result);
    }

    #endregion
}
