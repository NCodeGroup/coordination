// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Hosting.Sessions;

/// <summary>
/// Supplies the jitter from the shared random number generator.
/// </summary>
internal sealed class RandomJitter : IJitter
{
    /// <inheritdoc />
    public double Next() => Random.Shared.NextDouble();
}
