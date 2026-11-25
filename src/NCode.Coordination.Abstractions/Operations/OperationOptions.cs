// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// The settings a provider understands beyond the contract's, by name.
/// </summary>
/// <remarks>
/// A provider honors each extension it knows and refuses any other; it never ignores one.
/// </remarks>
public sealed class OperationOptions
{
    /// <summary>
    /// Gets the extensions, each a provider-prefixed name with a text value, none by default.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// There are too many entries, a name is not a valid name, or a value is null or too long.
    /// </exception>
    public ImmutableDictionary<string, string> Extensions
    {
        get;
        init
        {
            field = NamedValues.Check(
                value,
                CoordinationLimits.MaxExtensions,
                CoordinationLimits.MaxExtensionValueLength,
                nameof(Extensions)
            );
        }
    } = ImmutableDictionary<string, string>.Empty;
}
