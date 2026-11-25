// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace NCode.Coordination;

/// <summary>
/// A change a record watch reports: a record written, a record deleted, or a resync.
/// </summary>
/// <remarks>
/// Create a change with <see cref="Written(StoredRecord)"/>, <see cref="Deleted(CoordinationKey, Revision)"/> or
/// <see cref="Resync(Revision)"/>. A resync means the subscriber may have missed changes: it rereads the records,
/// then applies the changes after this revision.
/// </remarks>
public sealed class RecordChange
{
    private RecordChange(
        RecordChangeKind kind,
        CoordinationKey? key,
        StoredRecord? record,
        Revision revision
    )
    {
        Kind = kind;
        Key = key;
        Record = record;
        Revision = revision;
    }

    /// <summary>
    /// Gets the kind of the change.
    /// </summary>
    public RecordChangeKind Kind { get; }

    /// <summary>
    /// Gets the key of the record that changed, or null for a resync.
    /// </summary>
    public CoordinationKey? Key { get; }

    /// <summary>
    /// Gets the record as written, or null when the change is not a write.
    /// </summary>
    public StoredRecord? Record { get; }

    /// <summary>
    /// Gets the revision of the change; for a resync, the revision after which the subscriber applies changes.
    /// </summary>
    public Revision Revision { get; }

    /// <summary>
    /// Gets a value indicating whether the change is a resync, which means the subscriber may have missed changes.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Key))]
    public bool IsResync => Kind == RecordChangeKind.Resync;

    /// <summary>
    /// Creates the change for a written record.
    /// </summary>
    /// <param name="record">The record as written.</param>
    /// <returns>The written change, with the record's key and revision.</returns>
    /// <exception cref="ArgumentNullException">The record is null.</exception>
    public static RecordChange Written(StoredRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new RecordChange(RecordChangeKind.Written, record.Key, record, record.Revision);
    }

    /// <summary>
    /// Creates the change for a deleted record.
    /// </summary>
    /// <param name="key">The key of the record.</param>
    /// <param name="revision">The revision of the deletion.</param>
    /// <returns>The deleted change.</returns>
    /// <exception cref="ArgumentNullException">The key or the revision is null.</exception>
    public static RecordChange Deleted(CoordinationKey key, Revision revision)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(revision);

        return new RecordChange(RecordChangeKind.Deleted, key, null, revision);
    }

    /// <summary>
    /// Creates the change that tells the subscriber it may have missed changes.
    /// </summary>
    /// <param name="revision">The revision after which the subscriber applies changes once it has reread the records.</param>
    /// <returns>The resync change.</returns>
    /// <exception cref="ArgumentNullException">The revision is null.</exception>
    public static RecordChange Resync(Revision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        return new RecordChange(RecordChangeKind.Resync, null, null, revision);
    }
}
