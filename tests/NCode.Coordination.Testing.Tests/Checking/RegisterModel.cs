// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Testing.Histories;

namespace NCode.Coordination.Testing.Checking.Tests;

/// <summary>
/// A register that holds an integer: an integer input writes it and <see cref="Read"/> returns it.
/// </summary>
internal sealed class RegisterModel : ISequentialModel<int>
{
    /// <summary>
    /// The input that reads the register.
    /// </summary>
    public const string Read = "read";

    /// <inheritdoc />
    public int Initial => 0;

    /// <inheritdoc />
    public bool TryApply(int state, HistoryOperation operation, [MaybeNullWhen(false)] out int next)
    {
        next = state;

        switch (operation.Input)
        {
            case int value:
                next = value;
                return true;

            case Read:
                return operation.IsIndeterminate
                    || (operation.Output is int output && output == state);

            default:
                return false;
        }
    }
}
