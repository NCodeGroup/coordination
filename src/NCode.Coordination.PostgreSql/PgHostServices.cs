// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NCode.Coordination.PostgreSql;

/// <summary>
/// What the provider takes from its host: the factory of its loggers and the clock that its timers and polls run on.
/// </summary>
/// <param name="LoggerFactory">The factory of the loggers that report the failures the provider's background work catches.</param>
/// <param name="Time">The clock that the provider's sweeper, hub and polls run on.</param>
internal sealed record PgHostServices(ILoggerFactory LoggerFactory, TimeProvider Time)
{
    /// <summary>
    /// Gets the services of a host that registered none: no logging and the system clock.
    /// </summary>
    public static PgHostServices Default { get; } =
        new(NullLoggerFactory.Instance, TimeProvider.System);

    /// <summary>
    /// Takes the logger factory and the clock from the host's services, each falling back to the default's when the host registered none.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <returns>The host's logger factory and clock.</returns>
    public static PgHostServices From(IServiceProvider services)
    {
        var loggerFactory =
            services.GetService(typeof(ILoggerFactory)) as ILoggerFactory ?? Default.LoggerFactory;
        var time = services.GetService(typeof(TimeProvider)) as TimeProvider ?? Default.Time;

        return new PgHostServices(loggerFactory, time);
    }
}
