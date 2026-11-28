// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// The kinds that a change item stores in the <c>kind</c> column of the <c>change_items</c> table.
/// </summary>
internal static class PgChangeKinds
{
    /// <summary>
    /// The kind that a change item stores for a record that was written.
    /// </summary>
    public const short Written = 1;

    /// <summary>
    /// The kind that a change item stores for a record that was deleted.
    /// </summary>
    public const short Deleted = 2;
}
