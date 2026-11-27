// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.PostgreSql.Tests.Conformance;

/// <summary>
/// Runs the record contract suite against PostgreSQL 19.
/// </summary>
public sealed class PostgreSql19RecordContractTests(PgServers servers) : RecordContractTests
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
