// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Composition;

/// <summary>
/// A validated value and the failures found while building it, in the order they were found.
/// </summary>
/// <typeparam name="T">The type of the validated value.</typeparam>
/// <param name="value">The validated value.</param>
/// <param name="failures">The failures found while building the value.</param>
internal sealed class Checked<T>(T value, ImmutableArray<string> failures)
{
    /// <summary>
    /// Gets the validated value.
    /// </summary>
    public T Value { get; } = value;

    /// <summary>
    /// Gets the failures found while building the value, in the order they were found.
    /// </summary>
    public ImmutableArray<string> Failures { get; } = failures;
}
