// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The state of the two records of a transaction in its sequential specification.
/// </summary>
/// <param name="First">The value of the first record, or null when it is absent.</param>
/// <param name="Second">The value of the second record, or null when it is absent.</param>
internal sealed record TransactionState(string? First, string? Second);
