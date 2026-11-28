// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using NCode.Coordination.PostgreSql.Schema;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgChannelsTests
{
    #region ForKeyspace Tests

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void ForKeyspace_AnyId_FitsAnIdentifier(long keyspaceId)
    {
        var channel = PgChannels.ForKeyspace(PgSchemaName.Default, keyspaceId);

        Assert.True(channel.Length <= 63);
        Assert.Equal(31, channel.Length);
        var pattern = new Regex("^[a-z0-9.]+$");
        Assert.Matches(pattern, channel);
        Assert.StartsWith($"ncode.{PgSchemaName.Default.Hash}.", channel, StringComparison.Ordinal);
    }

    [Fact]
    public void ForKeyspace_NegativeId_UsesItsBits()
    {
        var channel = PgChannels.ForKeyspace(PgSchemaName.Default, -1L);

        Assert.EndsWith(".ffffffffffffffff", channel, StringComparison.Ordinal);
    }

    [Fact]
    public void ForKeyspace_DefaultSchemaAndKeyspaceFive_IsTheKnownName()
    {
        var channel = PgChannels.ForKeyspace(PgSchemaName.Default, 5);

        Assert.Equal("ncode.cbbeb3cc.0000000000000005", channel);
    }

    #endregion

    #region ChangePayload Tests

    [Fact]
    public void ChangePayload_Revision_PrefixesC()
    {
        var payload = PgChannels.ChangePayload(42);

        Assert.Equal("c:42", payload);
    }

    #endregion

    #region TopicPayload Tests

    [Fact]
    public void TopicPayload_TokenAndSequence_JoinsThem()
    {
        var payload = PgChannels.TopicPayload("abc", 7);

        Assert.Equal("t:abc:7", payload);
    }

    #endregion

    #region TryParseWake Tests

    [Fact]
    public void TryParseWake_Change_ReadsTheRevision()
    {
        var parsed = PgChannels.TryParseWake("c:42", out var wake);

        Assert.True(parsed);
        Assert.Null(wake.TopicToken);
        Assert.Equal(42L, wake.Number);
    }

    [Fact]
    public void TryParseWake_Topic_ReadsTokenAndSequence()
    {
        var parsed = PgChannels.TryParseWake("t:abc_-9:7", out var wake);

        Assert.True(parsed);
        Assert.Equal("abc_-9", wake.TopicToken);
        Assert.Equal(7L, wake.Number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("c:")]
    [InlineData("c:x")]
    [InlineData("t:abc")]
    [InlineData("x:1")]
    public void TryParseWake_Malformed_ReturnsFalse(string payload)
    {
        var parsed = PgChannels.TryParseWake(payload, out _);

        Assert.False(parsed);
    }

    #endregion

    #region IsChannel Tests

    [Fact]
    public void IsChannel_ForKeyspace_IsTrue()
    {
        var channel = PgChannels.ForKeyspace(PgSchemaName.Default, 5);

        var isChannel = PgChannels.IsChannel(channel);

        Assert.True(isChannel);
    }

    [Fact]
    public void IsChannel_Quote_IsFalse()
    {
        var isChannel = PgChannels.IsChannel("ncode.01234567.0123456789abcdef\"");

        Assert.False(isChannel);
    }

    #endregion
}
