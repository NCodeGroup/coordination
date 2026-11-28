// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using NCode.Coordination.PostgreSql.Subscriptions;
using NCode.Coordination.PostgreSql.Tests;

namespace NCode.Coordination.PostgreSql.Notifications.Tests;

public sealed class PgNotificationServiceTests
{
    private static PgNotificationService CreateService(PgOfflineServer offline)
    {
        var hub = new PgLogHub(
            offline.Source,
            offline.Keyspaces,
            offline.Settings,
            TimeProvider.System,
            NullLoggerFactory.Instance
        );

        return new PgNotificationService(offline.Server, offline.Keyspaces, offline.Settings, hub);
    }

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_PayloadTooLong_Throws()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = new Keyspace("prod").CreateKey("t");
        var payload = new byte[CoordinationLimits.MaxPayloadLength + 1];

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.PublishAsync(topic, payload, null, cancellationToken)
        );

        Assert.Equal("payload", exception.ParamName);
    }

    [Fact]
    public async Task PublishAsync_Extension_ThrowsUnsupported()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = new Keyspace("prod").CreateKey("t");
        var extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s");
        var options = new OperationOptions { Extensions = extensions };
        byte[] payload = [1];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.PublishAsync(topic, payload, options, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_Extension_ThrowsUnsupportedOnFirstMove()
    {
        await using var offline = new PgOfflineServer();
        var service = CreateService(offline);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = new Keyspace("prod").CreateKey("t");
        var extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s");
        var options = new OperationOptions { Extensions = extensions };

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
        {
            await foreach (var _ in service.SubscribeAsync(topic, options, cancellationToken)) { }
        });

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
        Assert.False(exception.OutcomeUnknown);
    }

    #endregion
}
