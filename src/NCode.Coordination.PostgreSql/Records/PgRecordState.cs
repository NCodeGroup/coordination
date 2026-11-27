// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Tells whether a stored record still holds, at an instant on the server's clock.
/// </summary>
internal enum PgRecordState
{
    /// <summary>The record has no deadline, or its deadline is after the instant.</summary>
    Live,

    /// <summary>The record's deadline is at or before the instant.</summary>
    Expired,
}
