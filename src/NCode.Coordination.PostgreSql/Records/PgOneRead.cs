// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds the row that <see cref="PgRecordTable.ReadOneAsync"/> read, with the instant the server read it at.
/// </summary>
/// <param name="Now">The server's instant.</param>
/// <param name="Value">The record's value.</param>
/// <param name="Revision">The keyspace's revision at which the record was written.</param>
/// <param name="Deadline">The instant the record expires, or null when it doesn't.</param>
/// <param name="Session">The id of the session the record is bound to, or null.</param>
/// <param name="SessionDeadline">The deadline of the session's row, or null when it is bound to none or the row is gone.</param>
internal sealed record PgOneRead(
    DateTime Now,
    byte[] Value,
    long Revision,
    DateTime? Deadline,
    string? Session,
    DateTime? SessionDeadline
)
{
    /// <summary>
    /// Tells whether the record holds at the instant that it was read.
    /// </summary>
    /// <returns>The state that <see cref="PgLiveness.StateOf"/> decides.</returns>
    public PgRecordState StateAt() => PgLiveness.StateOf(Deadline, Session, SessionDeadline, Now);
}
