// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Validation;

namespace NCode.Coordination;

/// <summary>
/// A record as stored: its key, its value and the revision of the write that produced it.
/// </summary>
public sealed class StoredRecord
{
    /// <summary>
    /// Creates a stored record.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="value">The value of the record. The type keeps its own copy.</param>
    /// <param name="revision">The revision of the write that produced the record.</param>
    /// <exception cref="ArgumentNullException">The key or the revision is null.</exception>
    /// <exception cref="ArgumentException">The value is longer than the limit.</exception>
    public StoredRecord(CoordinationKey key, ReadOnlyMemory<byte> value, Revision revision)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(revision);

        var copy = ByteValue.Copy(value, CoordinationLimits.MaxValueLength, nameof(value));

        Key = key;
        Value = copy;
        Revision = revision;
    }

    /// <summary>
    /// Gets the key of the record.
    /// </summary>
    public CoordinationKey Key { get; }

    /// <summary>
    /// Gets the value of the record, opaque bytes the caller encrypts where they must not be readable.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <summary>
    /// Gets the revision of the write that produced the record.
    /// </summary>
    public Revision Revision { get; }
}
