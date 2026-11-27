// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.Resp.Tests.Conformance;

/// <summary>
/// Runs the election contract suite against the Garnet cluster of three nodes.
/// </summary>
public sealed class GarnetClusterElectionContractTests(RespServers servers) : ElectionContractTests
{
    /// <inheritdoc />
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) =>
        RespConformance.CreateTargetAsync(
            servers.GarnetCluster,
            RespProfiles.Garnet,
            cancellationToken
        );
}
