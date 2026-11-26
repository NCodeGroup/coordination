// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Resp.Tests.Servers;
using NCode.Coordination.Testing;

namespace NCode.Coordination.Resp.Tests.Conformance;

public sealed class ValkeyRecordContractTests(RespServers servers) : RecordContractTests
{
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) => RespConformance.CreateTargetAsync(servers.Valkey, RespProfiles.Valkey, cancellationToken);
}
