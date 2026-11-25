// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// What an operation is: its primitive and the guarantee dimensions it carries.
/// </summary>
public sealed class OperationDefinition
{
    /// <summary>
    /// Creates the definition of an operation.
    /// </summary>
    /// <param name="operation">The operation defined.</param>
    /// <param name="primitive">The primitive the operation belongs to.</param>
    /// <param name="dimensions">The names of the guarantee dimensions the operation carries.</param>
    /// <exception cref="ArgumentNullException">The operation, the primitive or the dimensions are null.</exception>
    /// <exception cref="ArgumentException">
    /// The operation's name does not start with the primitive's name and a '.', or a dimension is null or not a valid
    /// name.
    /// </exception>
    public OperationDefinition(
        Operation operation,
        Primitive primitive,
        params IEnumerable<string> dimensions
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(primitive);

        if (!operation.Name.StartsWith(primitive.Name + ".", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An operation's name starts with its primitive's name and a '.'.",
                nameof(operation)
            );
        }

        var copy = ElementList.Copy(dimensions, nameof(dimensions));

        foreach (var dimension in copy)
        {
            VocabularyName.Validate(dimension, nameof(dimensions));
        }

        Operation = operation;
        Primitive = primitive;
        Dimensions = copy;
    }

    /// <summary>
    /// Gets the operation this definition describes.
    /// </summary>
    public Operation Operation { get; }

    /// <summary>
    /// Gets the primitive the operation belongs to.
    /// </summary>
    public Primitive Primitive { get; }

    /// <summary>
    /// Gets the guarantee dimensions the operation carries: a guarantee declared for the primitive in one of these
    /// dimensions holds for the operation.
    /// </summary>
    public ImmutableArray<string> Dimensions { get; }
}
