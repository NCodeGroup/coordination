// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// Gives features the one coordination session the host holds for its lifetime.
/// </summary>
public interface IHostSessionService
{
    /// <summary>
    /// Gets where the host's session stands.
    /// </summary>
    HostSessionState State { get; }

    /// <summary>
    /// Gets the session the host holds, or null unless a session is held and its <see cref="HostSession.Ended" />
    /// token is not cancelled.
    /// </summary>
    HostSession? Current { get; }

    /// <summary>
    /// Gets the session the host holds, waiting for the host to open one when it holds none.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>
    /// The current session at once when there is one, otherwise the next session the host opens.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The host has stopped, including when the wait was already pending as it stopped.
    /// </exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    ValueTask<HostSession> WaitForSessionAsync(CancellationToken cancellationToken);
}
