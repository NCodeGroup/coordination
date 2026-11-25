// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Testing.Models;

/// <summary>
/// The state of one semaphore in its sequential specification.
/// </summary>
/// <param name="FirstOwner">The owner value of the holder of slot 0, or null when the slot is free.</param>
/// <param name="FirstToken">The fencing token value of the holder of slot 0, or 0 when the slot is free.</param>
/// <param name="FirstFloor">The highest fencing token value granted for slot 0.</param>
/// <param name="SecondOwner">The owner value of the holder of slot 1, or null when the slot is free.</param>
/// <param name="SecondToken">The fencing token value of the holder of slot 1, or 0 when the slot is free.</param>
/// <param name="SecondFloor">The highest fencing token value granted for slot 1.</param>
/// <param name="Unknown">The number of slots held by acquires that never returned, which are not given a slot.</param>
internal sealed record SemaphoreState(
    string? FirstOwner,
    long FirstToken,
    long FirstFloor,
    string? SecondOwner,
    long SecondToken,
    long SecondFloor,
    int Unknown
);
