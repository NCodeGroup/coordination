// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;

namespace NCode.Coordination.Telemetry;

/// <summary>
/// An operation in flight: the names it is recorded under, its span and when it started.
/// </summary>
/// <param name="Provider">The name of the provider that serves the operation.</param>
/// <param name="Primitive">The primitive the operation belongs to.</param>
/// <param name="Operation">The operation.</param>
/// <param name="Activity">The operation's span, or null when nothing listens.</param>
/// <param name="Started">The timestamp at which the operation started.</param>
internal readonly record struct OperationMeasurement(
    string Provider,
    Primitive Primitive,
    Operation Operation,
    Activity? Activity,
    long Started
);
