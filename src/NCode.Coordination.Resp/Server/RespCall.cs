// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// The checks and conversions every RESP operation makes before it reaches the server.
/// </summary>
internal static class RespCall
{
    /// <summary>
    /// Checks the cancellation token and the options of an operation.
    /// </summary>
    /// <param name="options">The options of the operation, or null for the defaults.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <exception cref="OperationCanceledException">The token is canceled.</exception>
    /// <exception cref="CoordinationException">
    /// The options hold an extension, which the RESP provider never honors.
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
                $"The RESP provider honors no option extension, and '{name}' was given."
            );
        }
    }

    /// <summary>
    /// Converts a time to live to whole milliseconds, rounded up, never down.
    /// </summary>
    /// <param name="timeToLive">The time to live.</param>
    /// <returns>The number of whole milliseconds that covers the time to live.</returns>
    /// <exception cref="ArgumentNullException">The time to live is null.</exception>
    public static long ToMilliseconds(TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(timeToLive);

        return (timeToLive.Duration.Ticks + TimeSpan.TicksPerMillisecond - 1)
            / TimeSpan.TicksPerMillisecond;
    }
}
