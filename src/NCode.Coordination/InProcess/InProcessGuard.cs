// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The checks every in-process operation makes before it touches state.
/// </summary>
internal static class InProcessGuard
{
    /// <summary>
    /// Checks the cancellation token and the options of an operation.
    /// </summary>
    /// <param name="options">The options of the operation, or null for the defaults.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <exception cref="OperationCanceledException">The token is canceled.</exception>
    /// <exception cref="CoordinationException">
    /// The options hold an extension, which the in-process provider never honors.
    /// </exception>
    public static void Check(OperationOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options is null)
        {
            return;
        }

        foreach (var name in options.Extensions.Keys)
        {
            throw new CoordinationException(
                CoordinationErrorCodes.Unsupported,
                outcomeUnknown: false,
                $"The in-process provider honors no option extension, and '{name}' was given."
            );
        }
    }

    /// <summary>
    /// Checks that a value is not longer than the contract allows.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">The name of the argument that holds the value.</param>
    /// <exception cref="ArgumentException">The value is longer than <see cref="CoordinationLimits.MaxValueLength"/>.</exception>
    public static void CheckValue(ReadOnlyMemory<byte> value, string paramName)
    {
        if (value.Length > CoordinationLimits.MaxValueLength)
        {
            throw new ArgumentException(
                $"A value has at most {CoordinationLimits.MaxValueLength} bytes.",
                paramName
            );
        }
    }
}
