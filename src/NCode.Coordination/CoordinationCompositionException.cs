// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;

namespace NCode.Coordination;

/// <summary>
/// The composition of providers, bindings and requirements cannot run: each failure says what to change.
/// </summary>
public sealed class CoordinationCompositionException : Exception
{
    /// <summary>
    /// Copies and validates the failures.
    /// </summary>
    /// <param name="failures">What to change, one entry for each problem.</param>
    /// <returns>The failures, as an immutable array.</returns>
    /// <exception cref="ArgumentNullException">The failures are null.</exception>
    /// <exception cref="ArgumentException">The failures are empty, or an entry is null, empty or white space.</exception>
    private static ImmutableArray<string> Copy(IEnumerable<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var copy = failures.ToImmutableArray();
        if (copy.IsEmpty)
        {
            throw new ArgumentException("At least one failure is required.", nameof(failures));
        }

        if (copy.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A failure must not be null, empty or white space.",
                nameof(failures)
            );
        }

        return copy;
    }

    /// <summary>
    /// Builds the exception's message from its failures.
    /// </summary>
    /// <param name="failures">What to change, one entry for each problem.</param>
    /// <returns>The message that lists every failure.</returns>
    private static string Describe(ImmutableArray<string> failures)
    {
        var builder = new StringBuilder("The coordination composition is invalid:");
        foreach (var failure in failures)
        {
            builder.Append("\n- ").Append(failure);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates a failure that lists every problem found.
    /// </summary>
    /// <param name="failures">What to change, one entry for each problem, in order.</param>
    /// <exception cref="ArgumentNullException">The failures are null.</exception>
    /// <exception cref="ArgumentException">The failures are empty, or an entry is null, empty or white space.</exception>
    public CoordinationCompositionException(IEnumerable<string> failures)
        : this(Copy(failures)) { }

    /// <summary>
    /// Creates a failure from failures that are already validated.
    /// </summary>
    /// <param name="failures">What to change, one entry for each problem, in order.</param>
    private CoordinationCompositionException(ImmutableArray<string> failures)
        : base(Describe(failures))
    {
        Failures = failures;
    }

    /// <summary>
    /// Gets what to change, one entry for each problem, in order.
    /// </summary>
    public ImmutableArray<string> Failures { get; }
}
