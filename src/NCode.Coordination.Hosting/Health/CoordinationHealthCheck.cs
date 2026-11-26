// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NCode.Coordination.Hosting.Health;

/// <summary>
/// Reports the host not ready while it holds no coordination session or the provider failed its last call.
/// </summary>
/// <param name="sessions">The host's session, whose state the check reads.</param>
/// <remarks>
/// The check makes no call of its own: the renewals of the session are the probe. It tells readiness only, because a
/// liveness probe never checks a dependency.
/// </remarks>
internal class CoordinationHealthCheck(IHostSessionService sessions) : IHealthCheck
{
    /// <summary>
    /// Gets the host's session, whose state the check reads.
    /// </summary>
    private IHostSessionService Sessions { get; } = sessions;

    /// <summary>
    /// Reads the state of the host's session.
    /// </summary>
    /// <param name="context">The context of the check, whose registration names the status of a failure.</param>
    /// <param name="cancellationToken">A token that cancels the check.</param>
    /// <returns>Healthy while the host holds its session, otherwise the registration's failure status.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var state = Sessions.State;

        var result = state switch
        {
            HostSessionState.Active => HealthCheckResult.Healthy(
                "The host holds its coordination session."
            ),
            HostSessionState.NotStarted => new HealthCheckResult(
                context.Registration.FailureStatus,
                "The host has not started its coordination session."
            ),
            HostSessionState.Opening => new HealthCheckResult(
                context.Registration.FailureStatus,
                "The host is opening its coordination session."
            ),
            HostSessionState.Unreachable => new HealthCheckResult(
                context.Registration.FailureStatus,
                "The coordination provider failed the host's last call."
            ),
            HostSessionState.Stopped => new HealthCheckResult(
                context.Registration.FailureStatus,
                "The host has closed its coordination session."
            ),
            _ => new HealthCheckResult(
                context.Registration.FailureStatus,
                $"The host's coordination session is in the state {state}."
            ),
        };

        return Task.FromResult(result);
    }
}
