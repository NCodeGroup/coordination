// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Hosting.Sessions;

/// <summary>
/// Supplies the random part of the wait between the host's attempts to open its session.
/// </summary>
internal interface IJitter
{
    /// <summary>
    /// Gets the next random fraction.
    /// </summary>
    /// <returns>A fraction from zero up to, but not including, one.</returns>
    double Next();
}
