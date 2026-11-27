// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds one row of the <c>records</c> table, as a read returns it.
/// </summary>
/// <param name="Index">The row's index value.</param>
/// <param name="Depth">The row's partition depth.</param>
/// <param name="Key">The row's key text.</param>
/// <param name="Value">The record's value.</param>
/// <param name="Revision">The keyspace's revision at which the record was written.</param>
/// <param name="Deadline">The instant on the server's clock at which the record expires, or null when it doesn't.</param>
/// <param name="Session">The id of the session the record is bound to, or null when it is bound to none.</param>
internal sealed record PgRecordRow(
    byte[] Index,
    short Depth,
    string Key,
    byte[] Value,
    long Revision,
    DateTime? Deadline,
    string? Session
)
{
    /// <summary>
    /// Tells whether the record still holds at an instant.
    /// </summary>
    /// <param name="now">The instant, on the server's clock.</param>
    /// <returns><see cref="PgRecordState.Expired"/> when the deadline is at or before the instant; otherwise
    /// <see cref="PgRecordState.Live"/>.</returns>
    public PgRecordState StateAt(DateTime now) =>
        Deadline is { } deadline && deadline <= now ? PgRecordState.Expired : PgRecordState.Live;
}
