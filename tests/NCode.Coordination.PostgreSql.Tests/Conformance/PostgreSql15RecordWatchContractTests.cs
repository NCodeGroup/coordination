// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.PostgreSql.Tests.Conformance;

/// <summary>
/// Runs the record watch contract suite against PostgreSQL 15.
/// </summary>
public sealed class PostgreSql15RecordWatchContractTests(PgServers servers)
    : RecordWatchContractTests
{
    /// <inheritdoc />
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) =>
        PgConformance.CreateTargetAsync(
            servers.PostgreSql15,
            PostgreSqlProfiles.Standalone,
            cancellationToken
        );
}
