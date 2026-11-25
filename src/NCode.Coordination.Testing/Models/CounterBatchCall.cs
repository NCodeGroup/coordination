// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A batch that adds to the first key of a pair, then to the second, checking each count against the limit.
/// </summary>
/// <param name="FirstAmount">The amount that the batch adds to the first key.</param>
/// <param name="SecondAmount">The amount that the batch adds to the second key.</param>
/// <param name="Limit">The limit that the batch checks each count against.</param>
internal sealed record CounterBatchCall(long FirstAmount, long SecondAmount, long Limit);
