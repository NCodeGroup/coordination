// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.Resp.Tests.Conformance;

/// <summary>
/// Runs the counter contract suite against the Valkey cluster of three nodes.
/// </summary>
public sealed class ValkeyClusterCounterContractTests(RespServers servers) : CounterContractTests
{
    /// <inheritdoc />
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) =>
        RespConformance.CreateTargetAsync(
            servers.ValkeyCluster,
            RespProfiles.Valkey,
            cancellationToken
        );
}
