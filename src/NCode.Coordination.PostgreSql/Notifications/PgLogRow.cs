// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Notifications;

/// <summary>
/// Holds one row of the result of the statement that reads a topic's log.
/// </summary>
/// <param name="Removed">The highest sequence that the sweep removed, or 0 without a topic row.</param>
/// <param name="Current">The topic's current sequence, or 0 without a topic row.</param>
/// <param name="Message">The message that the row holds, or null when the topic has no message after the cursor.</param>
internal readonly record struct PgLogRow(long Removed, long Current, PgMessage? Message);
