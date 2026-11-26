// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NCode.Coordination.Hosting.Health;

namespace NCode.Coordination;

/// <summary>
/// Adds the host's coordination health check to an application's health checks.
/// </summary>
public static class CoordinationHealthChecksBuilderExtensions
{
    /// <summary>
    /// The name the coordination health check is registered under by default.
    /// </summary>
    public const string DefaultName = "coordination";

    /// <summary>
    /// The tag the coordination health check carries by default, which selects it for a readiness probe.
    /// </summary>
    public const string ReadyTag = "ready";

    extension(IHealthChecksBuilder builder)
    {
        /// <summary>
        /// Adds a readiness check that reports the host not ready while it holds no coordination session or the
        /// provider failed its last call.
        /// </summary>
        /// <returns>The builder, for chaining.</returns>
        /// <remarks>
        /// The check is registered under <see cref="DefaultName" /> with the <see cref="ReadyTag" /> tag. It tells
        /// readiness only and is not meant for liveness.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The builder is null.</exception>
        public IHealthChecksBuilder AddCoordinationCheck() =>
            builder.AddCoordinationCheck(DefaultName, ReadyTag);

        /// <summary>
        /// Adds a check that reports the host not ready while it holds no coordination session or the provider
        /// failed its last call.
        /// </summary>
        /// <param name="name">The name the check is registered under.</param>
        /// <param name="tags">The tags of the check.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <remarks>
        /// The status of an unhealthy check is the failure status of its registration, which the default is
        /// unhealthy.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The builder or the tags are null.</exception>
        /// <exception cref="ArgumentException">The name is null, empty or white space.</exception>
        public IHealthChecksBuilder AddCoordinationCheck(
            string name,
            params IEnumerable<string> tags
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(tags);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var registration = new HealthCheckRegistration(
                name,
                x =>
                {
                    var hostSession = x.GetRequiredService<IHostSessionService>();
                    return new CoordinationHealthCheck(hostSession);
                },
                null,
                tags
            );

            return builder.Add(registration);
        }
    }
}
