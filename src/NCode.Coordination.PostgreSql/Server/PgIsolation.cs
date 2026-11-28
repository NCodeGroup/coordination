// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// The isolation of a short transaction.
/// </summary>
internal enum PgIsolation
{
    /// <summary>
    /// A read-write transaction at <c>READ COMMITTED</c>.
    /// </summary>
    ReadCommitted,

    /// <summary>
    /// A read-only transaction at <c>REPEATABLE READ</c>, whose reads share one snapshot.
    /// </summary>
    RepeatableReadReadOnly,
}
