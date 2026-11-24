// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// One kind of coordination a provider can offer, named by an open vocabulary.
/// </summary>
/// <remarks>
/// Two primitives are equal when their names are equal. A name is lowercase letters and digits, with a single '.' or
/// '-' between them, and at most 128 characters, so a provider can define a primitive of its own.
/// </remarks>
public sealed record Primitive
{
    /// <summary>
    /// Creates a primitive.
    /// </summary>
    /// <param name="name">
    /// The name: lowercase letters and digits, with a single '.' or '-' between them, at most 128 characters.
    /// </param>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name is not a valid name.</exception>
    public Primitive(string name)
    {
        Name = VocabularyName.Validate(name, nameof(name));
    }

    /// <summary>
    /// Gets the name of the primitive.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Returns the name of the primitive.
    /// </summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
