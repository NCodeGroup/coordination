// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The type of the value a <see cref="KeySegment"/> holds.
/// </summary>
public enum KeySegmentKind
{
    /// <summary>
    /// A string of well-formed UTF-16 text.
    /// </summary>
    Text,

    /// <summary>
    /// A signed 64-bit integer.
    /// </summary>
    Number,

    /// <summary>
    /// A globally unique identifier.
    /// </summary>
    Uuid,
}
