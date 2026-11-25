// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A write a transaction applies to one record: a put or a delete.
/// </summary>
/// <remarks>
/// Create a write with <see cref="Put(CoordinationKey, ReadOnlyMemory{byte}, RecordLifetime)"/> or
/// <see cref="Delete(CoordinationKey)"/>.
/// </remarks>
public sealed class RecordWrite
{
    private RecordWrite(
        RecordWriteKind kind,
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime? lifetime
    )
    {
        Kind = kind;
        Key = key;
        Value = value;
        Lifetime = lifetime;
    }

    /// <summary>
    /// Gets the kind of the write.
    /// </summary>
    public RecordWriteKind Kind { get; }

    /// <summary>
    /// Gets the key of the record the write changes.
    /// </summary>
    public CoordinationKey Key { get; }

    /// <summary>
    /// Gets the value to write; empty for a delete.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <summary>
    /// Gets how long the written record lasts, or null for a delete.
    /// </summary>
    public RecordLifetime? Lifetime { get; }

    /// <summary>
    /// Creates the write that stores a value under a key.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="lifetime">How long the record lasts; it replaces the lifetime of an existing record.</param>
    /// <returns>The write.</returns>
    /// <exception cref="ArgumentNullException">The key or the lifetime is null.</exception>
    /// <exception cref="ArgumentException">The value is longer than the limit.</exception>
    public static RecordWrite Put(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lifetime);

        if (value.Length > CoordinationLimits.MaxValueLength)
        {
            throw new ArgumentException(
                $"A value has at most {CoordinationLimits.MaxValueLength} bytes.",
                nameof(value)
            );
        }

        return new RecordWrite(RecordWriteKind.Put, key, value, lifetime);
    }

    /// <summary>
    /// Creates the write that deletes a record.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <returns>The write.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public static RecordWrite Delete(CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new RecordWrite(RecordWriteKind.Delete, key, ReadOnlyMemory<byte>.Empty, null);
    }
}
