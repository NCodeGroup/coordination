// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.PostgreSql.Tests.Conformance;

/// <summary>
/// Runs the claim contract suite against PostgreSQL 19.
/// </summary>
public sealed class PostgreSql19ClaimContractTests(PgServers servers) : ClaimContractTests
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
