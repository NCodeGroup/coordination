// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Histories;

/// <summary>
/// A call that moves the clock forward while nothing else is in flight.
/// </summary>
/// <param name="Duration">The time that the clock advances by.</param>
internal sealed record ClockAdvance(TimeSpan Duration);
