// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgListenerTests
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Timeout=1;Database=coordination;Username=u;Password=p";

    private static PgListener CreateListener(PgSource source)
    {
        var events = new PgListenerEvents((_, _) => { }, () => { }, () => { });
        return new PgListener(source, events, TimeProvider.System);
    }

    #region ListenAsync Tests

    [Fact]
    public async Task ListenAsync_ChannelWithAQuote_Throws()
    {
        await using var source = PgSource.FromConnectionString(UnreachableConnectionString);
        await using var listener = CreateListener(source);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await listener.ListenAsync("ncode.\"; DROP TABLE x; --", CancellationToken.None)
        );
    }

    [Fact]
    public async Task ListenAsync_ServerUnreachable_ThrowsUnavailable()
    {
        await using var source = PgSource.FromConnectionString(UnreachableConnectionString);
        await using var listener = CreateListener(source);

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await listener.ListenAsync("ncode.0123abcd.0123456789abcdef", CancellationToken.None)
        );

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
