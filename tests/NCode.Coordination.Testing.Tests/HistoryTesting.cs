// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Tests;

/// <summary>
/// Builds the calls of a history that a test checks.
/// </summary>
internal static class HistoryTesting
{
    /// <summary>
    /// The partition of the calls that <see cref="Returned"/> and <see cref="Indeterminate"/> build.
    /// </summary>
    public const string Partition = "p";

    /// <summary>
    /// Builds a call that returned, made by the first client and stamped 1 and 2.
    /// </summary>
    /// <param name="input">The call: the operation and its arguments.</param>
    /// <param name="output">The result the call returned.</param>
    /// <returns>The call.</returns>
    public static HistoryOperation Returned(object input, object? output) =>
        Operation(0, Partition, input, 1, 2, output);

    /// <summary>
    /// Builds a call whose outcome is indeterminate, made by the first client and stamped 1.
    /// </summary>
    /// <param name="input">The call: the operation and its arguments.</param>
    /// <returns>The call.</returns>
    public static HistoryOperation Indeterminate(object input) =>
        new(0, Partition, input, 1, long.MaxValue, HistoryOutcome.Indeterminate, null);

    /// <summary>
    /// Builds a call that returned.
    /// </summary>
    /// <param name="client">The client that made the call.</param>
    /// <param name="partition">The key whose history the call belongs to.</param>
    /// <param name="input">The call: the operation and its arguments.</param>
    /// <param name="invoked">The stamp at which the call was made.</param>
    /// <param name="responded">The stamp at which the call returned.</param>
    /// <param name="output">The result the call returned.</param>
    /// <returns>The call.</returns>
    public static HistoryOperation Operation(
        int client,
        string partition,
        object input,
        long invoked,
        long responded,
        object? output
    ) => new(client, partition, input, invoked, responded, HistoryOutcome.Returned, output);
}
