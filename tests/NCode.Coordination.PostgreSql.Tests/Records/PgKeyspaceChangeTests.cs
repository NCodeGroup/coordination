// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Records.Tests;

public sealed class PgKeyspaceChangeTests
{
    private const long KeyspaceId = 7;

    private static DateTime Now { get; } = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    private static PgKeyspaceChange CreateChange(long revision = 5)
    {
        var origin = new PgChangeOrigin(KeyspaceId, revision, Now);
        return new PgKeyspaceChange(PgSettings.Default, origin);
    }

    private static PgRecordPut CreatePut(CoordinationKey key, DateTime? deadline) =>
        new(key, [9], deadline, null);

    private static CoordinationKey CreateKey(string name)
    {
        var keyspace = new Keyspace("prod", "records");
        return new CoordinationKey(keyspace, name);
    }

    private static PgRecordRow CreateRow(CoordinationKey key)
    {
        var index = PgKeys.Index(key);
        var keyText = PgKeys.KeyText(key);
        var deadline = Now.AddSeconds(-1);
        return new PgRecordRow(
            index,
            (short)key.PartitionDepth,
            keyText,
            [1, 2, 3],
            4,
            deadline,
            null
        );
    }

    #region Begin Tests

    [Fact]
    public void Begin_Twice_TakesConsecutiveRevisions()
    {
        var change = CreateChange(5);

        var first = change.Begin();
        var second = change.Begin();

        Assert.Equal(6, first);
        Assert.Equal(7, second);
        Assert.Equal(7, change.Revision);
        Assert.False(change.IsEmpty);
    }

    #endregion

    #region Expire Tests

    [Fact]
    public void Expire_NoRows_TakesNoRevision()
    {
        var change = CreateChange(5);

        change.Expire([]);

        Assert.Equal(5, change.Revision);
        Assert.True(change.IsEmpty);
    }

    #endregion

    #region Statements Tests

    [Fact]
    public void Statements_Empty_IsEmpty()
    {
        var change = CreateChange();

        var statements = change.Statements("channel");

        Assert.Empty(statements);
    }

    [Fact]
    public void Statements_OneWrite_UpdatesTheRevisionAndNotifiesOnce()
    {
        var key = CreateKey("a");
        var deadline = Now.AddMinutes(1);
        var put = CreatePut(key, deadline);
        var change = CreateChange(5);
        var revision = change.Begin();
        change.Put(put);

        var statements = change.Statements("channel");

        Assert.Equal(5, statements.Length);
        Assert.Contains("INSERT INTO \"coordination\".records", statements[0].Sql);
        Assert.Equal(revision, statements[0].Arguments[5].Value);
        Assert.Contains("INSERT INTO \"coordination\".changes", statements[1].Sql);
        var expectedDeadline = Now.AddMinutes(5);
        Assert.Equal(expectedDeadline, statements[1].Arguments[2].Value);
        Assert.Contains("INSERT INTO \"coordination\".change_items", statements[2].Sql);
        Assert.Contains("UPDATE \"coordination\".keyspaces", statements[3].Sql);
        Assert.Equal(KeyspaceId, statements[3].Arguments[0].Value);
        Assert.Equal(revision, statements[3].Arguments[1].Value);
        var notifyCount = statements.Count(s => s.Sql.Contains("pg_notify"));
        Assert.Equal(1, notifyCount);
        Assert.Equal("channel", statements[4].Arguments[0].Value);
        Assert.Equal("c:6", statements[4].Arguments[1].Value);
    }

    [Fact]
    public void Statements_ExpiryThenWrite_LogsTwoEntries()
    {
        var key = CreateKey("a");
        var put = CreatePut(key, null);
        var change = CreateChange(5);
        var row = CreateRow(key);
        change.Expire([row]);
        var revision = change.Begin();
        change.Put(put);

        var statements = change.Statements("channel");

        Assert.Equal(8, statements.Length);
        Assert.Contains("DELETE FROM \"coordination\".records", statements[0].Sql);
        Assert.Equal(6L, statements[1].Arguments[1].Value);
        Assert.Contains("INSERT INTO \"coordination\".change_items", statements[2].Sql);
        Assert.Contains("INSERT INTO \"coordination\".records", statements[3].Sql);
        Assert.Equal(7L, statements[4].Arguments[1].Value);
        var changesCount = statements.Count(s => s.Sql.Contains("\"coordination\".changes"));
        Assert.Equal(2, changesCount);
        Assert.Equal(revision, statements[6].Arguments[1].Value);
        Assert.Equal("c:7", statements[7].Arguments[1].Value);
    }

    #endregion

    #region Put Tests

    [Fact]
    public void Put_BeforeBegin_Throws()
    {
        var key = CreateKey("a");
        var put = CreatePut(key, null);
        var change = CreateChange();

        Assert.Throws<InvalidOperationException>(() => change.Put(put));
    }

    #endregion

    #region EndSession Tests

    [Fact]
    public void EndSession_Twice_TakesOneRevision()
    {
        var change = CreateChange(5);

        change.EndSession("s-1");
        change.EndSession("s-1");

        Assert.Equal(6, change.Revision);

        var statements = change.Statements("channel");

        Assert.Equal(5, statements.Length);
        Assert.Contains("DELETE FROM \"coordination\".sessions", statements[0].Sql);
        Assert.Equal("s-1", statements[0].Arguments[1].Value);
        Assert.Contains("INSERT INTO \"coordination\".ended_sessions", statements[1].Sql);
        Assert.Equal("s-1", statements[1].Arguments[1].Value);
        Assert.Equal(6L, statements[1].Arguments[2].Value);
        Assert.Contains("INSERT INTO \"coordination\".changes", statements[2].Sql);
        Assert.Equal(6L, statements[2].Arguments[1].Value);
        Assert.Equal("s-1", statements[2].Arguments[3].Value);
        Assert.Equal("c:6", statements[4].Arguments[1].Value);
    }

    #endregion

    #region Write Tests

    [Fact]
    public void Write_Alone_TakesNoRevisionAndSendsNoNotification()
    {
        var change = CreateChange(5);

        var statement = new PgStatement("SELECT 1");

        change.Write(statement);

        Assert.Equal(5, change.Revision);
        Assert.False(change.IsEmpty);

        var statements = change.Statements("channel");

        Assert.Single(statements);
        Assert.Equal("SELECT 1", statements[0].Sql);
    }

    #endregion

    #region NameGroup Tests

    [Fact]
    public void NameGroup_CurrentEntry_LogsTheGroup()
    {
        var change = CreateChange(5);
        change.Begin();

        change.NameGroup([1, 2], 3);

        var statements = change.Statements("channel");

        Assert.Contains("INSERT INTO \"coordination\".changes", statements[0].Sql);
        Assert.Equal(new byte[] { 1, 2 }, statements[0].Arguments[4].Value);
        Assert.Equal((short)3, statements[0].Arguments[5].Value);
    }

    #endregion
}
