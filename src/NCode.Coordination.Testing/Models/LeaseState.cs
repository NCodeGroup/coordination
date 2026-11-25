// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The state of one lease in its sequential specification.
/// </summary>
/// <param name="Owner">The owner value of the holder, or null when no one holds the lease.</param>
/// <param name="Token">The fencing token value of the holder, or 0 when it is unknown or no one holds the lease.</param>
/// <param name="Floor">The highest fencing token value granted so far.</param>
/// <param name="Now">The provider's clock, as the sum of the clock advances.</param>
/// <param name="Until">The time on the provider's clock at which the hold ends.</param>
internal sealed record LeaseState(
    string? Owner,
    long Token,
    long Floor,
    TimeSpan Now,
    TimeSpan Until
);
