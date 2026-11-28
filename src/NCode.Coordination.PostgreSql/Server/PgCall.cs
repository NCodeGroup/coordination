// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// The checks and conversions every PostgreSQL operation makes before it reaches the server.
/// </summary>
internal static class PgCall
{
    /// <summary>
    /// Gets the message that refuses a value longer than <see cref="CoordinationLimits.MaxValueLength"/> bytes.
    /// </summary>
    public static string ValueTooLongMessage { get; } =
        $"A value has at most {CoordinationLimits.MaxValueLength} bytes.";

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
    /// Checks that a value is no longer than the largest value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="paramName">The name of the parameter that holds the value.</param>
    /// <exception cref="ArgumentException">The value is longer than <see cref="CoordinationLimits.MaxValueLength"/> bytes.</exception>
    public static void CheckValue(ReadOnlyMemory<byte> value, string paramName)
    {
        if (value.Length > CoordinationLimits.MaxValueLength)
        {
            throw new ArgumentException(ValueTooLongMessage, paramName);
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
        return (duration.Ticks + TimeSpan.TicksPerMicrosecond - 1) / TimeSpan.TicksPerMicrosecond;
    }

    /// <summary>
    /// Adds a time to live to a time, in whole microseconds rounded up, never down.
    /// </summary>
    /// <param name="now">The time to add to.</param>
    /// <param name="timeToLive">The time to live.</param>
    /// <returns>The time when the time to live ends.</returns>
    /// <exception cref="ArgumentNullException">The time to live is null.</exception>
    public static DateTime DeadlineAfter(DateTime now, TimeToLive timeToLive)
    {
        var microseconds = ToMicroseconds(timeToLive);
        return now.AddTicks(microseconds * TimeSpan.TicksPerMicrosecond);
    }

    /// <summary>
    /// Converts whole microseconds to a duration.
    /// </summary>
    /// <param name="microseconds">The number of microseconds.</param>
    /// <returns>The duration of that many microseconds.</returns>
    public static TimeSpan FromMicroseconds(long microseconds)
    {
        return TimeSpan.FromTicks(microseconds * TimeSpan.TicksPerMicrosecond);
    }
}
