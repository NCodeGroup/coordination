// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Leases;

/// <summary>
/// The kinds that a grant row stores in the <c>kind</c> column of the <c>grants</c> table.
/// </summary>
internal static class PgGrantKinds
{
    /// <summary>
    /// The kind of the grant rows of leases.
    /// </summary>
    public const short Lease = 1;

    /// <summary>
    /// The kind of the grant rows of semaphores.
    /// </summary>
    public const short Semaphore = 2;

    /// <summary>
    /// The kind of the grant rows of elections.
    /// </summary>
    public const short Election = 3;
}
