// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.Resp.Tests.Conformance;

public sealed class ValkeyMembershipContractTests(RespServers servers) : MembershipContractTests
{
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) => RespConformance.CreateTargetAsync(servers.Valkey, RespProfiles.Valkey, cancellationToken);
}
