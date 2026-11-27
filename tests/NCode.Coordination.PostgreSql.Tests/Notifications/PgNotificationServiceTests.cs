// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Keys;
using NCode.Coordination.PostgreSql.Schema;
using NCode.Coordination.PostgreSql.Server;
using NCode.Coordination.PostgreSql.Subscriptions;

namespace NCode.Coordination.PostgreSql.Notifications.Tests;

public sealed class PgNotificationServiceTests
{
    private static async Task WithServiceAsync(Func<PgNotificationService, Task> test)
    {
        await using var source = PgSource.FromConnectionString(
            "Host=127.0.0.1;Port=1;Database=coordination;Username=u;Password=p;Timeout=1"
        );
        var settings = PgSettings.Default;
        var server = new PgServer(source, settings);
        var keyspaces = new PgKeyspaces(server, PgSchemaName.Default);
        await using var hub = new PgLogHub(source, keyspaces, settings, TimeProvider.System);

        await test(new PgNotificationService(server, keyspaces, settings, hub));
    }

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_PayloadTooLong_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = new Keyspace("prod").CreateKey("t");
        var payload = new byte[CoordinationLimits.MaxPayloadLength + 1];
        await WithServiceAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await service.PublishAsync(topic, payload, null, cancellationToken)
            );
        });
    }

    [Fact]
    public async Task PublishAsync_Extension_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = new Keyspace("prod").CreateKey("t");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
                await service.PublishAsync(topic, new byte[] { 1 }, options, cancellationToken)
            );

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_Extension_ThrowsUnsupportedOnFirstMove()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = new Keyspace("prod").CreateKey("t");
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };
        await WithServiceAsync(async service =>
        {
            var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            {
                await foreach (var _ in service.SubscribeAsync(topic, options, cancellationToken))
                { }
            });

            Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
            Assert.False(exception.OutcomeUnknown);
        });
    }

    #endregion
}
