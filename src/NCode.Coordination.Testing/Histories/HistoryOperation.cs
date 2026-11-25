// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Histories;

/// <summary>
/// One call that a client made, with the points in the history where it was invoked and where it responded.
/// </summary>
/// <param name="Client">The client that made the call, or <see cref="HistoryOperation.TestClient"/> for the test itself.</param>
/// <param name="Partition">The key whose history the call belongs to, or <see cref="HistoryOperation.EveryPartition"/> for every key.</param>
/// <param name="Input">The call: the operation and its arguments.</param>
/// <param name="Invoked">The stamp on the history's clock at which the call was made.</param>
/// <param name="Responded">
/// The stamp on the history's clock at which the call returned or failed, or <see cref="long.MaxValue"/> when the
/// outcome is indeterminate.
/// </param>
/// <param name="Outcome">How the call ended.</param>
/// <param name="Output">The result the call returned, null when it returned none, or the exception it failed with.</param>
internal sealed record HistoryOperation(
    int Client,
    string Partition,
    object Input,
    long Invoked,
    long Responded,
    HistoryOutcome Outcome,
    object? Output
)
{
    /// <summary>
    /// The client of a call that the test itself made rather than one of the run's clients.
    /// </summary>
    public const int TestClient = -1;

    /// <summary>
    /// The partition of a call that belongs to the history of every key.
    /// </summary>
    public const string EveryPartition = "*";

    /// <summary>
    /// Whether the call may have applied at any point after it was made, with no result known.
    /// </summary>
    public bool IsIndeterminate => Outcome == HistoryOutcome.Indeterminate;
}
