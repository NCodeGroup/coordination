// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Polls the server until a condition holds, for the waits and watches that do not subscribe, waiting on the
/// provider's clock.
/// </summary>
/// <param name="time">The clock that the waits between two checks run on.</param>
/// <exception cref="ArgumentNullException">The clock is null.</exception>
internal sealed class PgPolling(TimeProvider time)
{
    /// <summary>
    /// Gets the time between two checks.
    /// </summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets the clock that the waits between two checks run on.
    /// </summary>
    private TimeProvider Time { get; } = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>
    /// Waits one <see cref="Interval"/> on the clock.
    /// </summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>A task that completes once the interval has passed.</returns>
    /// <exception cref="OperationCanceledException">The token cancels before the interval has passed.</exception>
    public async ValueTask DelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Interval, Time, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the condition at once, then every <see cref="Interval"/>, until it holds. It never gives up on its own.
    /// </summary>
    /// <param name="condition">Reads the server and returns <see langword="true"/> once the condition holds.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="ArgumentNullException">The condition is null.</exception>
    /// <exception cref="OperationCanceledException">The token cancels before the condition holds.</exception>
    public async ValueTask UntilAsync(
        Func<CancellationToken, ValueTask<bool>> condition,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(condition);

        while (!await condition(cancellationToken).ConfigureAwait(false))
        {
            await DelayAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
