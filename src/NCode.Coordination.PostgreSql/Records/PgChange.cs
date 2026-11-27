// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.PostgreSql.Subscriptions;

namespace NCode.Coordination.PostgreSql.Records;

/// <summary>
/// One entry of a keyspace's change log: the revision a change took, the keys it wrote or deleted, the group it named
/// and the session it ended.
/// </summary>
/// <param name="Number">The entry's revision.</param>
/// <param name="EndedSession">The id of the session the entry ended, or null.</param>
/// <param name="GroupIndex">The index value of the group the entry names, or null.</param>
/// <param name="GroupDepth">The partition depth of the group the entry names, or null.</param>
/// <param name="Items">The keys the entry wrote or deleted, in order.</param>
/// <param name="EndedKeys">
/// For an entry that ended a session, the keys still bound to it in the snapshot the entry was read in, as deleted
/// items; empty otherwise.
/// </param>
internal sealed record PgChange(
    long Number,
    string? EndedSession,
    byte[]? GroupIndex,
    short? GroupDepth,
    ImmutableArray<PgChangeItem> Items,
    ImmutableArray<PgChangeItem> EndedKeys
) : IPgLogEntry;
