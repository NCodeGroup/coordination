// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The outcome of a transaction.
/// </summary>
public enum TransactionStatus
{
    /// <summary>
    /// Every condition held, and the writes on success applied.
    /// </summary>
    Succeeded,

    /// <summary>
    /// A condition did not hold, and the writes on failure applied.
    /// </summary>
    Failed,

    /// <summary>
    /// The writes of the branch the conditions chose bind a record to a session that has ended, so nothing was written.
    /// </summary>
    SessionEnded,
}
