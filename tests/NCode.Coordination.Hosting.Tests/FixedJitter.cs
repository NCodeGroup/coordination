// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Hosting.Sessions;

namespace NCode.Coordination.Hosting.Tests;

/// <summary>
/// Supplies the same fraction every time, so a test knows each wait exactly.
/// </summary>
/// <param name="value">The fraction that <see cref="Next" /> returns.</param>
internal sealed class FixedJitter(double value) : IJitter
{
    /// <inheritdoc />
    public double Next() => value;
}
