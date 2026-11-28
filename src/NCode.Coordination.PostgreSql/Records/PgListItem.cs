// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds one row that the list statement returned.
/// </summary>
/// <param name="KeyspaceRevision">The keyspace's revision.</param>
/// <param name="Now">The server's instant.</param>
/// <param name="Row">The record's row, or null when the row holds no record.</param>
internal readonly record struct PgListItem(long KeyspaceRevision, DateTime Now, PgRecordRow? Row)
{
    /// <summary>
    /// Tells whether the record holds at the instant that it was read.
    /// </summary>
    /// <returns>
    /// The state that <see cref="PgLiveness.StateOf"/> decides for the row; <see cref="PgRecordState.Live"/> when
    /// there is none.
    /// </returns>
    public PgRecordState StateAt() => Row?.StateAt(Now) ?? PgRecordState.Live;
}
