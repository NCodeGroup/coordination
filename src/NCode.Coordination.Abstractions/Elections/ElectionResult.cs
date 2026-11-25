// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination;

/// <summary>
/// The result of trying to lead a role: elected with its leadership, led by another node, or the session ended.
/// </summary>
/// <remarks>
/// Create an elected result with <see cref="Elected(Leadership)"/>; <see cref="Led"/> and
/// <see cref="SessionEnded"/> are shared.
/// </remarks>
public sealed class ElectionResult
{
    /// <summary>
    /// Gets the result for a role another node leads.
    /// </summary>
    public static ElectionResult Led { get; } = new(ElectionStatus.Led, null);

    /// <summary>
    /// Gets the result for a session that has ended.
    /// </summary>
    public static ElectionResult SessionEnded { get; } = new(ElectionStatus.SessionEnded, null);

    private ElectionResult(ElectionStatus status, Leadership? leadership)
    {
        Status = status;
        Leadership = leadership;
    }

    /// <summary>
    /// Gets the outcome of the attempt.
    /// </summary>
    public ElectionStatus Status { get; }

    /// <summary>
    /// Gets the leadership of the elected leader, or null when the caller was not elected.
    /// </summary>
    public Leadership? Leadership { get; }

    /// <summary>
    /// Gets a value indicating whether the caller was elected.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Leadership))]
    public bool IsElected => Status == ElectionStatus.Elected;

    /// <summary>
    /// Creates the result for an elected leader.
    /// </summary>
    /// <param name="leadership">The leadership of the elected leader.</param>
    /// <returns>The elected result.</returns>
    /// <exception cref="ArgumentNullException">The leadership is null.</exception>
    public static ElectionResult Elected(Leadership leadership)
    {
        ArgumentNullException.ThrowIfNull(leadership);

        return new ElectionResult(ElectionStatus.Elected, leadership);
    }
}
