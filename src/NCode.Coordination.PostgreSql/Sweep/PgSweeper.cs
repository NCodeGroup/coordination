// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using NCode.Coordination.PostgreSql.Logging;

namespace NCode.Coordination.PostgreSql.Sweep;

/// <summary>
/// Runs the sweep on a periodic timer until it is disposed, and sweeps again after a sweep fails.
/// </summary>
/// <param name="sweep">The sweep to run at each tick.</param>
/// <param name="interval">The time between the sweeps.</param>
/// <param name="time">The clock that times the interval.</param>
/// <param name="logger">The logger that reports a failed sweep.</param>
/// <exception cref="ArgumentNullException">The sweep, the clock or the logger are null.</exception>
internal sealed class PgSweeper(
    Func<CancellationToken, ValueTask> sweep,
    TimeSpan interval,
    TimeProvider time,
    ILogger<PgSweeper> logger
) : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the lock that orders starting, cancelling and reading the loop.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets or sets a value that indicates whether the loop was started.
    /// </summary>
    private bool Started { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the sweeper was cancelled.
    /// </summary>
    private bool Cancelled { get; set; }

    /// <summary>
    /// Gets the source of the token that ends the loop.
    /// </summary>
    private CancellationTokenSource Cancellation { get; } = new();

    /// <summary>
    /// Gets or sets the task of the loop.
    /// </summary>
    private Task Loop { get; set; } = Task.CompletedTask;

    /// <summary>
    /// Gets or sets the timer that ticks the sweeps, which <see cref="Start" /> creates.
    /// </summary>
    private PeriodicTimer? Timer { get; set; }

    /// <summary>
    /// Gets the sweep to run at each tick.
    /// </summary>
    private Func<CancellationToken, ValueTask> Sweep { get; } =
        sweep ?? throw new ArgumentNullException(nameof(sweep));

    /// <summary>
    /// Gets the time between the sweeps.
    /// </summary>
    private TimeSpan Interval { get; } = interval;

    /// <summary>
    /// Gets the clock that times the interval.
    /// </summary>
    private TimeProvider Time { get; } = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>
    /// Gets the logger that reports a failed sweep.
    /// </summary>
    private ILogger<PgSweeper> Logger { get; } =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Starts the loop that sweeps at each tick of a periodic timer, unless it was already started or disposed.
    /// </summary>
    public void Start()
    {
        lock (Gate)
        {
            if (Cancelled || Started)
            {
                return;
            }

            Started = true;
            var timer = new PeriodicTimer(Interval, Time);
            Timer = timer;
            var token = Cancellation.Token;
            Loop = Task.Run(() => RunAsync(timer, token).AsTask(), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task loop;
        PeriodicTimer? timer;
        lock (Gate)
        {
            Cancel();
            loop = Loop;
            timer = Timer;
        }

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The sweeper is stopped, and the loop ended with its cancellation.
        }

        timer?.Dispose();
        Cancellation.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (Gate)
        {
            Cancel();
            Timer?.Dispose();
            Cancellation.Dispose();
        }
    }

    /// <summary>
    /// Marks the sweeper cancelled and cancels the source of the loop's token. The caller holds the lock.
    /// </summary>
    private void Cancel()
    {
        if (Cancelled)
        {
            return;
        }

        Cancelled = true;
        Cancellation.Cancel();
    }

    /// <summary>
    /// Sweeps at each tick of the timer, until the token is cancelled.
    /// </summary>
    /// <param name="timer">The timer that ticks the sweeps.</param>
    /// <param name="cancellationToken">The token that ends the loop, which is the sweeper's own.</param>
    /// <returns>A task that completes when the loop ends; its own cancellation ends it quietly.</returns>
    private async ValueTask RunAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SweepQuietlyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The sweeper is stopped; a sweep that the stop interrupted needs no report.
        }
    }

    /// <summary>
    /// Runs one sweep and swallows its failure, unless the sweeper was cancelled.
    /// </summary>
    /// <param name="cancellationToken">The token that ends the loop, which is the sweeper's own.</param>
    /// <returns>A task that completes when the sweep ends, or fails when it was cancelled.</returns>
    private async ValueTask SweepQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Sweep(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The next tick sweeps again, and one failed sweep must not end the sweeping.
            Logger.SweepFailed(exception);
        }
    }
}
