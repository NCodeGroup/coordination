// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests.Servers;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgColumnsTests(PgServers servers)
{
    private static async ValueTask<(string? Missing, string? Present)> ReadTextsAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(
            r => (PgColumns.ReadNullableText(r, 0), PgColumns.ReadNullableText(r, 1)),
            cancellationToken
        );
        return rows[0];
    }

    private static async ValueTask<(byte[]? Missing, byte[]? Present)> ReadBytesAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(
            r => (PgColumns.ReadNullableBytes(r, 0), PgColumns.ReadNullableBytes(r, 1)),
            cancellationToken
        );
        return rows[0];
    }

    private static async ValueTask<(string[]? Missing, string[]? Present)> ReadTextArraysAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(
            r => (PgColumns.ReadNullableTexts(r, 0), PgColumns.ReadNullableTexts(r, 1)),
            cancellationToken
        );
        return rows[0];
    }

    private static async ValueTask<(
        DateTime? Missing,
        DateTime? Present
    )> ReadNullableInstantsAsync(PgResults results, CancellationToken cancellationToken)
    {
        var rows = await results.ReadAsync(
            r => (PgColumns.ReadNullableUtc(r, 0), PgColumns.ReadNullableUtc(r, 1)),
            cancellationToken
        );
        return rows[0];
    }

    private static async ValueTask<DateTime> ReadInstantAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        var rows = await results.ReadAsync(r => PgColumns.ReadUtc(r, 0), cancellationToken);
        return rows[0];
    }

    #region ReadNullableText Tests

    [PgServerFact]
    public async Task ReadNullableText_NullAndNonNull_ReturnsNullAndTheText()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
        var server = new PgServer(source, PgSettings.Default);
        PgStatement[] statements = [new PgStatement("SELECT NULL::text, 'abc'::text")];

        var row = await server.ExecuteAsync(statements, ReadTextsAsync, cancellationToken);

        Assert.Null(row.Missing);
        Assert.Equal("abc", row.Present);
    }

    #endregion

    #region ReadNullableBytes Tests

    [PgServerFact]
    public async Task ReadNullableBytes_NullAndNonNull_ReturnsNullAndTheBytes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
        var server = new PgServer(source, PgSettings.Default);
        PgStatement[] statements = [new PgStatement("SELECT NULL::bytea, '\\x0102'::bytea")];
        byte[] expected = [1, 2];

        var row = await server.ExecuteAsync(statements, ReadBytesAsync, cancellationToken);

        Assert.Null(row.Missing);
        Assert.Equal(expected, row.Present);
    }

    #endregion

    #region ReadNullableTexts Tests

    [PgServerFact]
    public async Task ReadNullableTexts_NullAndNonNull_ReturnsNullAndTheTexts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
        var server = new PgServer(source, PgSettings.Default);
        PgStatement[] statements =
        [
            new PgStatement("SELECT NULL::text[], ARRAY['a', 'b']::text[]"),
        ];
        string[] expected = ["a", "b"];

        var row = await server.ExecuteAsync(statements, ReadTextArraysAsync, cancellationToken);

        Assert.Null(row.Missing);
        Assert.Equal(expected, row.Present);
    }

    #endregion

    #region ReadNullableUtc Tests

    [PgServerFact]
    public async Task ReadNullableUtc_NullAndNonNull_ReturnsNullAndTheInstantOfKindUtc()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
        var server = new PgServer(source, PgSettings.Default);
        PgStatement[] statements =
        [
            new PgStatement("SELECT NULL::timestamptz, '2026-01-02 03:04:05+00'::timestamptz"),
        ];
        var expected = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var row = await server.ExecuteAsync(
            statements,
            ReadNullableInstantsAsync,
            cancellationToken
        );

        Assert.Null(row.Missing);
        Assert.Equal(expected, row.Present);
        Assert.Equal(DateTimeKind.Utc, row.Present?.Kind);
    }

    #endregion

    #region ReadUtc Tests

    [PgServerFact]
    public async Task ReadUtc_Now_ReturnsAnInstantOfKindUtc()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = PgSource.FromConnectionString(servers.PostgreSql19);
        var server = new PgServer(source, PgSettings.Default);
        PgStatement[] statements = [new PgStatement("SELECT now()")];

        var instant = await server.ExecuteAsync(statements, ReadInstantAsync, cancellationToken);

        Assert.Equal(DateTimeKind.Utc, instant.Kind);
    }

    #endregion
}
