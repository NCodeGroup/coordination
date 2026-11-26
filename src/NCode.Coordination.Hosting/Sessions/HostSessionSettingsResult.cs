// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Hosting.Sessions;

/// <summary>
/// The outcome of checking the host's options: the settings when every rule passed, or one failure for each property
/// that broke its rule.
/// </summary>
internal sealed class HostSessionSettingsResult
{
    /// <summary>
    /// Gets the settings, or null when a property broke its rule.
    /// </summary>
    public HostSessionSettings? Settings { get; init; }

    /// <summary>
    /// Gets one failure for each property that broke its rule, or none when the settings were built.
    /// </summary>
    public required IReadOnlyList<string> Failures { get; init; }
}
