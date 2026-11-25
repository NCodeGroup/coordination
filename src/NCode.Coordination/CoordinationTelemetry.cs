// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The names a host subscribes to for NCode.Coordination's metrics and traces.
/// </summary>
public static class CoordinationTelemetry
{
    /// <summary>
    /// The name of the meter that records the operations' durations.
    /// </summary>
    public const string MeterName = "NCode.Coordination";

    /// <summary>
    /// The name of the activity source that records the operations' spans.
    /// </summary>
    public const string ActivitySourceName = "NCode.Coordination";
}
