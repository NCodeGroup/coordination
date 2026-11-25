// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A candidate for a singleton role: the session it campaigns with, and the value followers read.
/// </summary>
public sealed class SingletonCandidate
{
    /// <summary>
    /// Creates a candidate.
    /// </summary>
    /// <param name="session">The session the candidate campaigns with, and whose end ends its leadership.</param>
    /// <exception cref="ArgumentNullException">The session is null.</exception>
    public SingletonCandidate(SessionId session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
    }

    /// <summary>
    /// Gets the session the candidate campaigns with.
    /// </summary>
    public SessionId Session { get; }

    /// <summary>
    /// Gets the value followers read, such as the leader's address, empty by default. The type keeps its own copy.
    /// </summary>
    /// <exception cref="ArgumentException">The value is longer than the limit.</exception>
    public ReadOnlyMemory<byte> Value
    {
        get;
        init
        {
            if (value.Length > CoordinationLimits.MaxValueLength)
            {
                throw new ArgumentException(
                    $"A value has at most {CoordinationLimits.MaxValueLength} bytes.",
                    nameof(Value)
                );
            }

            field = value.ToArray();
        }
    }
}
