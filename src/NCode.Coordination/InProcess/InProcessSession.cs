// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// A live session of the in-process provider.
/// </summary>
/// <param name="id">The identity of the session.</param>
/// <param name="timeToLive">The time to live the session was granted, counted from each keep-alive.</param>
internal class InProcessSession(SessionId id, TimeToLive timeToLive)
{
    /// <summary>
    /// Gets the identity of the session.
    /// </summary>
    public SessionId Id { get; } = id;

    /// <summary>
    /// Gets the time to live the session was granted, counted from each keep-alive.
    /// </summary>
    public TimeToLive TimeToLive { get; } = timeToLive;

    /// <summary>
    /// Gets or sets the deadline that ends the session, or null before it is scheduled.
    /// </summary>
    public InProcessDeadline? Deadline { get; set; }

    /// <summary>
    /// Gets the task that completes when the session ends.
    /// </summary>
    public TaskCompletionSource Ended { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
