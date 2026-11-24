// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// What a provider offers for one primitive: the guarantees it keeps, or the reason it does not support the primitive.
/// </summary>
/// <remarks>
/// Create one with <see cref="Supported"/> or <see cref="Unsupported"/>. A supported primitive has no reason, and an
/// unsupported one has no guarantees.
/// </remarks>
public sealed class PrimitiveSupport
{
    private PrimitiveSupport(
        Primitive primitive,
        ImmutableArray<Guarantee> guarantees,
        string? unsupportedReason
    )
    {
        Primitive = primitive;
        Guarantees = guarantees;
        UnsupportedReason = unsupportedReason;
    }

    /// <summary>
    /// Gets the primitive this support describes.
    /// </summary>
    public Primitive Primitive { get; }

    /// <summary>
    /// Gets the guarantees the provider keeps for the primitive, none when the primitive is not supported.
    /// </summary>
    public ImmutableArray<Guarantee> Guarantees { get; }

    /// <summary>
    /// Gets why the provider does not support the primitive, or null when it does.
    /// </summary>
    public string? UnsupportedReason { get; }

    /// <summary>
    /// Gets a value that tells whether the provider supports the primitive.
    /// </summary>
    [MemberNotNullWhen(false, nameof(UnsupportedReason))]
    public bool IsSupported => UnsupportedReason is null;

    /// <summary>
    /// Describes a primitive the provider supports.
    /// </summary>
    /// <param name="primitive">The primitive the provider supports.</param>
    /// <param name="guarantees">The guarantees the provider keeps for the primitive, possibly none.</param>
    /// <returns>The support for the primitive.</returns>
    /// <exception cref="ArgumentNullException">The primitive or the guarantees are null.</exception>
    /// <exception cref="ArgumentException">A guarantee is null.</exception>
    public static PrimitiveSupport Supported(
        Primitive primitive,
        params IEnumerable<Guarantee> guarantees
    )
    {
        ArgumentNullException.ThrowIfNull(primitive);

        var kept = ElementList.Copy(guarantees, nameof(guarantees));

        return new PrimitiveSupport(primitive, kept, unsupportedReason: null);
    }

    /// <summary>
    /// Describes a primitive the provider does not support.
    /// </summary>
    /// <param name="primitive">The primitive the provider does not support.</param>
    /// <param name="reason">Why the provider does not support the primitive, in plain words.</param>
    /// <returns>The support for the primitive.</returns>
    /// <exception cref="ArgumentNullException">The primitive or the reason is null.</exception>
    /// <exception cref="ArgumentException">The reason is empty or only white space.</exception>
    public static PrimitiveSupport Unsupported(Primitive primitive, string reason)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new PrimitiveSupport(primitive, [], reason);
    }
}
