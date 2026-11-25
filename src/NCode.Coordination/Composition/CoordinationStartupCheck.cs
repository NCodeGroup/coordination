// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;

namespace NCode.Coordination.Composition;

/// <summary>
/// Composes the coordination and probes its providers before any hosted service starts, so the host's start fails on a
/// composition that cannot keep its requirements or a server that doesn't keep what its provider declares.
/// </summary>
/// <param name="composition">The application's composition.</param>
internal sealed class CoordinationStartupCheck(CoordinationComposition composition)
    : IHostedLifecycleService
{
    /// <summary>
    /// Gets the composition to compose and probe.
    /// </summary>
    private CoordinationComposition Composition { get; } = composition;

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) =>
        Composition.EnsureStartedAsync(cancellationToken).AsTask();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
