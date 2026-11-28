// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// Holds the first page of an ended session's keys, with the cursor that read it.
/// </summary>
/// <param name="Cursor">The cursor that started the page.</param>
/// <param name="Keys">The keys of the page, as deleted items in index order.</param>
internal readonly record struct PgEndedKeysPage(
    PgEndedKeysCursor Cursor,
    ImmutableArray<PgChangeItem> Keys
);
