// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// A call on one of the two keys of a run that records every call in one partition.
/// </summary>
/// <param name="Key">The index of the key, 0 or 1.</param>
/// <param name="Call">The input that the model of the key takes.</param>
internal sealed record KeyedCall(int Key, object Call);
