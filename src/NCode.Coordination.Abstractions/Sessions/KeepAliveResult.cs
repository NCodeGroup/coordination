// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination;

/// <summary>
/// The result of renewing a session: renewed with the time to live the provider granted, or ended.
/// </summary>
/// <remarks>
/// Create a renewed result with <see cref="Renewed(TimeToLive)"/>; <see cref="Ended"/> is shared.
/// </remarks>
public sealed class KeepAliveResult
{
    /// <summary>
    /// Gets the result for a session that has ended.
    /// </summary>
    public static KeepAliveResult Ended { get; } = new(KeepAliveStatus.Ended, null);

    private KeepAliveResult(KeepAliveStatus status, TimeToLive? timeToLive)
    {
        Status = status;
        TimeToLive = timeToLive;
    }

    /// <summary>
    /// Gets the outcome of the renewal.
    /// </summary>
    public KeepAliveStatus Status { get; }

    /// <summary>
    /// Gets the time to live the provider granted for the renewed session, or null when the session ended.
    /// </summary>
    public TimeToLive? TimeToLive { get; }

    /// <summary>
    /// Gets a value indicating whether the session was renewed.
    /// </summary>
    [MemberNotNullWhen(true, nameof(TimeToLive))]
    public bool IsRenewed => Status == KeepAliveStatus.Renewed;

    /// <summary>
    /// Creates the result for a renewed session.
    /// </summary>
    /// <param name="timeToLive">The time to live the provider granted.</param>
    /// <returns>The renewed result.</returns>
    /// <exception cref="ArgumentNullException">The time to live is null.</exception>
    public static KeepAliveResult Renewed(TimeToLive timeToLive)
    {
        ArgumentNullException.ThrowIfNull(timeToLive);

        return new KeepAliveResult(KeepAliveStatus.Renewed, timeToLive);
    }
}
