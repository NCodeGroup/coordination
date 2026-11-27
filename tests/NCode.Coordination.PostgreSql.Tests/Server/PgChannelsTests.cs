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
        Assert.Matches(new Regex("^[a-z0-9.]+$"), channel);
        Assert.StartsWith($"ncode.{PgSchemaName.Default.Hash}.", channel, StringComparison.Ordinal);
    }

    [Fact]
    public void ForKeyspace_NegativeId_UsesItsBits()
    {
        var channel = PgChannels.ForKeyspace(PgSchemaName.Default, -1L);

        Assert.EndsWith(".ffffffffffffffff", channel, StringComparison.Ordinal);
    }

    #endregion

    #region ChangePayload Tests

    [Fact]
    public void ChangePayload_Revision_PrefixesC()
    {
        Assert.Equal("c:42", PgChannels.ChangePayload(42));
    }

    #endregion

    #region TopicPayload Tests

    [Fact]
    public void TopicPayload_TokenAndSequence_JoinsThem()
    {
        Assert.Equal("t:abc:7", PgChannels.TopicPayload("abc", 7));
    }

    #endregion

    #region TryParseWake Tests

    [Fact]
    public void TryParseWake_Change_ReadsTheRevision()
    {
        Assert.True(PgChannels.TryParseWake("c:42", out var wake));

        Assert.Null(wake.TopicToken);
        Assert.Equal(42L, wake.Number);
    }

    [Fact]
    public void TryParseWake_Topic_ReadsTokenAndSequence()
    {
        Assert.True(PgChannels.TryParseWake("t:abc_-9:7", out var wake));

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
        Assert.False(PgChannels.TryParseWake(payload, out _));
    }

    #endregion

    #region IsChannel Tests

    [Fact]
    public void IsChannel_ForKeyspace_IsTrue()
    {
        Assert.True(PgChannels.IsChannel(PgChannels.ForKeyspace(PgSchemaName.Default, 5)));
    }

    [Fact]
    public void IsChannel_Quote_IsFalse()
    {
        Assert.False(PgChannels.IsChannel("ncode.01234567.0123456789abcdef\""));
    }

    #endregion
}
