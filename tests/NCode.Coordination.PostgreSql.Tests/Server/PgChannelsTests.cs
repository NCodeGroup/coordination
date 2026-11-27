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
}
