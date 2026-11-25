// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// A condition on one record that a transaction checks before it chooses which writes to apply.
/// </summary>
/// <remarks>
/// Create a condition with <see cref="Absent(CoordinationKey)"/>, <see cref="RevisionEquals(CoordinationKey, Revision)"/>
/// or <see cref="ValueEquals(CoordinationKey, ReadOnlyMemory{byte})"/>.
/// </remarks>
public sealed class RecordCondition
{
    private RecordCondition(
        RecordConditionKind kind,
        CoordinationKey key,
        Revision? revision,
        ReadOnlyMemory<byte> value
    )
    {
        Kind = kind;
        Key = key;
        Revision = revision;
        Value = value;
    }

    /// <summary>
    /// Gets the kind of the condition.
    /// </summary>
    public RecordConditionKind Kind { get; }

    /// <summary>
    /// Gets the key of the record the condition checks.
    /// </summary>
    public CoordinationKey Key { get; }

    /// <summary>
    /// Gets the revision the record must have, or null when the condition does not compare a revision.
    /// </summary>
    public Revision? Revision { get; }

    /// <summary>
    /// Gets the value the record must have; empty when the condition does not compare a value.
    /// </summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <summary>
    /// Creates the condition that holds when the record does not exist.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    public static RecordCondition Absent(CoordinationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new RecordCondition(
            RecordConditionKind.Absent,
            key,
            null,
            ReadOnlyMemory<byte>.Empty
        );
    }

    /// <summary>
    /// Creates the condition that holds when the record exists with the given revision.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="revision">The revision the record must have.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="ArgumentNullException">The key or the revision is null.</exception>
    public static RecordCondition RevisionEquals(CoordinationKey key, Revision revision)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(revision);

        return new RecordCondition(
            RecordConditionKind.RevisionEquals,
            key,
            revision,
            ReadOnlyMemory<byte>.Empty
        );
    }

    /// <summary>
    /// Creates the condition that holds when the record exists with the given value.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="value">The value the record must have.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="ArgumentNullException">The key is null.</exception>
    /// <exception cref="ArgumentException">The value is longer than the limit.</exception>
    public static RecordCondition ValueEquals(CoordinationKey key, ReadOnlyMemory<byte> value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (value.Length > CoordinationLimits.MaxValueLength)
        {
            throw new ArgumentException(
                $"A value has at most {CoordinationLimits.MaxValueLength} bytes.",
                nameof(value)
            );
        }

        return new RecordCondition(RecordConditionKind.ValueEquals, key, null, value);
    }
}
