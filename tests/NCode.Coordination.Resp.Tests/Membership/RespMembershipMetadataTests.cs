// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Resp.Membership.Tests;

public sealed class RespMembershipMetadataTests
{
    #region Encode Tests

    [Fact]
    public void Encode_Empty_ReturnsEmpty()
    {
        var text = RespMembershipMetadata.Encode(ImmutableDictionary<string, string>.Empty);

        Assert.Equal("", text);
    }

    [Fact]
    public void Encode_TwoEntries_ListsThemInOrdinalOrderOfNames()
    {
        var metadata = ImmutableDictionary<string, string>
            .Empty.Add("c", "")
            .Add("a.b", "10.0.0.4:5000");

        var text = RespMembershipMetadata.Encode(metadata);

        Assert.Equal("3:a.b13:10.0.0.4:50001:c0:", text);
    }

    #endregion

    #region TryDecode Tests

    [Fact]
    public void TryDecode_EncodedWithColonsPipesAndEmptyValue_ReturnsTheSameEntries()
    {
        var metadata = ImmutableDictionary<string, string>
            .Empty.Add("a.b", "10.0.0.4:5000")
            .Add("c", "x|y=z")
            .Add("d", "");

        var decoded = RespMembershipMetadata.TryDecode(
            RespMembershipMetadata.Encode(metadata),
            out var result
        );

        Assert.True(decoded);
        Assert.Equal(metadata, result);
    }

    [Fact]
    public void TryDecode_Empty_ReturnsEmptyMetadata()
    {
        var decoded = RespMembershipMetadata.TryDecode("", out var result);

        Assert.True(decoded);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void TryDecode_LengthPastTheEnd_ReturnsFalse()
    {
        var decoded = RespMembershipMetadata.TryDecode("5:ab", out var result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    [Fact]
    public void TryDecode_NonDigitLength_ReturnsFalse()
    {
        var decoded = RespMembershipMetadata.TryDecode("x:a1:b", out var result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    [Fact]
    public void TryDecode_MissingColon_ReturnsFalse()
    {
        var decoded = RespMembershipMetadata.TryDecode("1a1b", out var result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    [Fact]
    public void TryDecode_NameWithoutValue_ReturnsFalse()
    {
        var decoded = RespMembershipMetadata.TryDecode("1:a", out var result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    [Fact]
    public void TryDecode_DuplicateName_ReturnsFalse()
    {
        var decoded = RespMembershipMetadata.TryDecode("1:a1:b1:a1:c", out var result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    #endregion
}
