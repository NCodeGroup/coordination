// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

// A sealed record class, not a readonly record struct: a struct's default instance would skip the
// constructor's validation.
/// <summary>
/// One operation of a primitive, named <c>&lt;primitive&gt;.&lt;operation&gt;</c> by an open vocabulary.
/// </summary>
/// <remarks>
/// Two operations are equal when their names are equal. A name is lowercase letters and digits, with a single '.' or
/// '-' between them, and at most 128 characters, so a provider can define an operation of its own.
/// </remarks>
public sealed record Operation
{
    /// <summary>
    /// Creates an operation.
    /// </summary>
    /// <param name="name">
    /// The name: lowercase letters and digits, with a single '.' or '-' between them, at most 128 characters.
    /// </param>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name is not a valid name.</exception>
    public Operation(string name)
    {
        Name = VocabularyName.Validate(name, nameof(name));
    }

    /// <summary>
    /// Gets the name of the operation.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Returns the name of the operation.
    /// </summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
