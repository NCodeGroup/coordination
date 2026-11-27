// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.PostgreSql.Tests.Conformance;

/// <summary>
/// Runs the set contract suite against PostgreSQL 19.
/// </summary>
public sealed class PostgreSql19SetContractTests(PgServers servers) : SetContractTests
{
    /// <inheritdoc />
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) =>
        PgConformance.CreateTargetAsync(
            servers.PostgreSql19,
            PostgreSqlProfiles.Standalone,
            cancellationToken
        );
}
