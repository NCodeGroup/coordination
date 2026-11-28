// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// One key that an entry of a keyspace's change log wrote or deleted.
/// </summary>
/// <param name="Kind">1 for a record written, 2 for a record deleted.</param>
/// <param name="Index">The key's index value.</param>
/// <param name="Depth">The key's partition depth.</param>
/// <param name="Key">The key's text.</param>
/// <param name="Value">The value written, or null for a deletion.</param>
internal sealed record PgChangeItem(
    short Kind,
    byte[] Index,
    short Depth,
    string Key,
    byte[]? Value
);
