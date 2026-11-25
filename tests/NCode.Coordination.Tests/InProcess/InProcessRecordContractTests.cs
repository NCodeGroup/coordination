// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing;

namespace NCode.Coordination.InProcess.Tests;

public sealed class InProcessRecordContractTests : RecordContractTests
{
    protected override ValueTask<ConformanceTarget> CreateTargetAsync(
        CancellationToken cancellationToken
    ) => InProcessConformance.CreateTargetAsync(cancellationToken);
}
