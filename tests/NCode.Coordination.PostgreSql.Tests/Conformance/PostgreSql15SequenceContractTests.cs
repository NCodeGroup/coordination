// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.PostgreSql.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.PostgreSql.Tests.Conformance;

/// <summary>
/// Runs the sequence contract suite against PostgreSQL 15.
/// </summary>
public sealed class PostgreSql15SequenceContractTests(PgServers servers) : SequenceContractTests
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
