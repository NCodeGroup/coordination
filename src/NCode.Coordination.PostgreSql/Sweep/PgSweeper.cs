// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Sweep;

/// <summary>
/// Runs the sweep passes on an interval until it is disposed.
/// </summary>
/// <param name="sweeps">The passes to run.</param>
/// <param name="interval">The time between the passes.</param>
/// <param name="time">The clock that times the interval.</param>
/// <exception cref="ArgumentNullException">The sweeps or the clock are null.</exception>
internal sealed class PgSweeper(PgSweeps sweeps, TimeSpan interval, TimeProvider time)
    : IAsyncDisposable,
        IDisposable
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
    /// Gets the passes to run.
    /// </summary>
    private PgSweeps Sweeps { get; } = sweeps ?? throw new ArgumentNullException(nameof(sweeps));

    /// <summary>
    /// Gets the clock that times the interval.
    /// </summary>
    private TimeProvider Time { get; } = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>
    /// Starts the loop that waits the interval and runs the passes, unless it was already started or disposed.
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
            var token = Cancellation.Token;
            Loop = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task loop;
        lock (Gate)
        {
            Cancel();
            loop = Loop;
        }

        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The sweeper is stopped, and the loop ended with its cancellation.
        }

        Cancellation.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (Gate)
        {
            Cancel();
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
    /// Waits the interval and runs the passes, until the token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">The token that ends the loop, which is the sweeper's own.</param>
    /// <returns>A task that completes when the loop ends; its own cancellation ends it quietly.</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, Time, cancellationToken).ConfigureAwait(false);
                await Sweeps.SweepAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The sweeper is stopped.
        }
    }
}
