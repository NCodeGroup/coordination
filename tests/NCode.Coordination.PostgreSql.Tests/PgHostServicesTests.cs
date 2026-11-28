// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PgHostServicesTests
{
    #region From Tests

    [Fact]
    public async Task From_ServicesHoldBoth_ReturnsTheHostsOwn()
    {
        var time = new FakeTimeProvider();
        var collector = new FakeLogCollector();
        var loggerFactory = new PgCollectingLoggerFactory(collector);
        var collection = new ServiceCollection();
        collection.AddSingleton<TimeProvider>(time);
        collection.AddSingleton<ILoggerFactory>(loggerFactory);
        await using var services = collection.BuildServiceProvider();

        var host = PgHostServices.From(services);

        Assert.Same(time, host.Time);
        Assert.Same(loggerFactory, host.LoggerFactory);
    }

    [Fact]
    public async Task From_NoServices_ReturnsTheDefaults()
    {
        var collection = new ServiceCollection();
        await using var services = collection.BuildServiceProvider();

        var host = PgHostServices.From(services);

        Assert.Same(TimeProvider.System, host.Time);
        Assert.Same(NullLoggerFactory.Instance, host.LoggerFactory);
    }

    #endregion
}
