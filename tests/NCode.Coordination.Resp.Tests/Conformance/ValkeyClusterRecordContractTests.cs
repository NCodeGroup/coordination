// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.Resp.Tests.Conformance;

/// <summary>
/// Runs the record contract suite against the Valkey cluster of three nodes.
/// </summary>
public sealed class ValkeyClusterRecordContractTests(RespServers servers) : RecordContractTests
{
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) =>
        RespConformance.CreateTargetAsync(
            servers.ValkeyCluster,
            RespProfiles.Valkey,
            cancellationToken
        );
}
