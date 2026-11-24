// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// What one feature of an application needs: a primitive and the guarantees it must keep, optionally on a keyspace.
/// </summary>
public sealed class FeatureRequirement
{
    /// <summary>
    /// Creates the requirement of a feature.
    /// </summary>
    /// <param name="feature">The name of the feature.</param>
    /// <param name="primitive">The primitive the feature needs.</param>
    /// <param name="guarantees">
    /// The guarantees the primitive must keep for the feature, possibly none, which asks for the primitive with no
    /// particular guarantee.
    /// </param>
    /// <exception cref="ArgumentNullException">The feature, the primitive or the guarantees are null.</exception>
    /// <exception cref="ArgumentException">The feature is not a valid name, or a guarantee is null.</exception>
    public FeatureRequirement(
        string feature,
        Primitive primitive,
        params IEnumerable<Guarantee> guarantees
    )
    {
        Feature = VocabularyName.Validate(feature, nameof(feature));
        ArgumentNullException.ThrowIfNull(primitive);
        Primitive = primitive;
        Guarantees = ElementList.Copy(guarantees, nameof(guarantees));
    }

    /// <summary>
    /// Gets the name of the feature.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// Gets the primitive the feature needs.
    /// </summary>
    public Primitive Primitive { get; }

    /// <summary>
    /// Gets the guarantees the primitive must keep for the feature, none when it needs no particular guarantee.
    /// </summary>
    public ImmutableArray<Guarantee> Guarantees { get; }

    /// <summary>
    /// Gets the keyspace the feature uses, or null for the composition's default binding, which is the default.
    /// </summary>
    public Keyspace? Keyspace { get; init; }
}
