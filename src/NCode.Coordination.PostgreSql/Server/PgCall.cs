// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// The checks and conversions every PostgreSQL operation makes before it reaches the server.
/// </summary>
internal static class PgCall
{
    /// <summary>
    /// Checks the cancellation token and the options of an operation.
    /// </summary>
    /// <param name="options">The options of the operation, or null for the defaults.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <exception cref="OperationCanceledException">The token is canceled.</exception>
    /// <exception cref="CoordinationException">
    /// The options hold an extension, which the PostgreSQL provider never honors.
    /// </exception>
    public static void Check(OperationOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options?.Extensions.Count > 0)
        {
            var name = options.Extensions.Keys.First();
            throw new CoordinationException(
                CoordinationErrorCodes.Unsupported,
                outcomeUnknown: false,
                $"The PostgreSQL provider honors no option extension, and '{name}' was given."
            );
        }
    }

    /// <summary>
    /// Converts a time to live to whole microseconds, rounded up, never down.
    /// </summary>
    /// <param name="timeToLive">The time to live.</param>
    /// <returns>The number of whole microseconds that covers the time to live.</returns>
    /// <exception cref="ArgumentNullException">The time to live is null.</exception>
    public static long ToMicroseconds(TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(timeToLive);

        return ToMicroseconds(timeToLive.Duration);
    }

    /// <summary>
    /// Converts a duration to whole microseconds, rounded up, never down.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The number of whole microseconds that covers the duration.</returns>
    public static long ToMicroseconds(TimeSpan duration)
    {
        return (duration.Ticks + 9) / 10;
    }
}
