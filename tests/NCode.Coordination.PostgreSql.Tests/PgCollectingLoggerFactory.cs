// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace NCode.Coordination.PostgreSql.Tests;

/// <summary>
/// A logger factory whose loggers all write to one collector, so a test sees what every component logged.
/// </summary>
/// <param name="collector">The collector that every logger writes to.</param>
internal sealed class PgCollectingLoggerFactory(FakeLogCollector collector) : ILoggerFactory
{
    /// <summary>
    /// Gets the collector that every logger writes to.
    /// </summary>
    private FakeLogCollector Collector { get; } =
        collector ?? throw new ArgumentNullException(nameof(collector));

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FakeLogger(Collector, categoryName);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    /// <inheritdoc />
    public void Dispose() { }
}
