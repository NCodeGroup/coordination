// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Tests.Servers;

/// <summary>
/// Describes what a test waits for in <c>pg_stat_activity</c>.
/// </summary>
/// <param name="Condition">The <c>WHERE</c> condition that selects the backends, with one parameter, <c>$1</c>.</param>
/// <param name="Argument">The value of <c>$1</c>.</param>
/// <param name="IsDone">Tests the count of the backends that match <paramref name="Condition"/>.</param>
/// <param name="Failure">The message of the failure when the wait times out.</param>
internal sealed record PgBackendWait(
    string Condition,
    string Argument,
    Func<long, bool> IsDone,
    string Failure
);
