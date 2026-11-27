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
    private readonly CancellationTokenSource _cancellation = new();
    private int _started;
    private int _cancelled;
    private Task _loop = Task.CompletedTask;

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
        if (Volatile.Read(ref _cancelled) != 0 || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var token = _cancellation.Token;
        _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Cancel();

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        _cancellation.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Cancel();
    }

    private void Cancel()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) == 0)
        {
            _cancellation.Cancel();
        }
    }

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
