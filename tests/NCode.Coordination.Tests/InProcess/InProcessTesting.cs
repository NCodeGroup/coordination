// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// Shares the start time, the keyspace and the fake clock of the in-process tests.
/// </summary>
internal static class InProcessTesting
{
    /// <summary>
    /// Gets the time the tests' fake clock starts at.
    /// </summary>
    public static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Gets the keyspace the tests use.
    /// </summary>
    public static Keyspace Keyspace { get; } = new("test");

    /// <summary>
    /// Creates a fake clock that starts at <see cref="Start" />.
    /// </summary>
    /// <returns>The fake clock.</returns>
    public static FakeTimeProvider CreateTime() => new(Start);
}
