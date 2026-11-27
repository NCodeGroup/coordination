// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Polls the server until a condition holds, for the waits and watches that do not subscribe.
/// </summary>
internal static class PgPolling
{
    /// <summary>
    /// Gets the time between two checks.
    /// </summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Checks the condition at once, then every <see cref="Interval"/>, until it holds. It never gives up on its own.
    /// </summary>
    /// <param name="condition">Reads the server and returns <see langword="true"/> once the condition holds.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="ArgumentNullException">The condition is null.</exception>
    /// <exception cref="OperationCanceledException">The token cancels before the condition holds.</exception>
    public static async ValueTask UntilAsync(
        Func<CancellationToken, ValueTask<bool>> condition,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(condition);

        while (!await condition(cancellationToken).ConfigureAwait(false))
        {
            await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);
        }
    }
}
