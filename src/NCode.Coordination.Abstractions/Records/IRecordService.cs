// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of records: small values under keys, each with a revision that changes on every write.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface IRecordService
{
    /// <summary>
    /// Creates a record when its key is absent.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordCreate"/>. It writes only when the key is absent, and otherwise the
    /// result is conflict. A record bound to an ended session is not written: the result is session ended. A value
    /// above <see cref="CoordinationLimits.MaxValueLength"/> bytes throws <see cref="ArgumentException"/>.
    /// The provider reads the value before the returned task completes, so the caller may reuse its buffer once it
    /// does.
    /// </remarks>
    /// <param name="key">The key of the record.</param>
    /// <param name="value">The value of the record.</param>
    /// <param name="lifetime">How long the record lasts.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Written with the new revision, conflict, or session ended.</returns>
    ValueTask<RecordWriteResult> CreateAsync(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads a record.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordRead"/>. The result is null when the key is absent.
    /// </remarks>
    /// <param name="key">The key of the record.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The record, or null when it is absent.</returns>
    ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Replaces a record when its revision is the expected one.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordCompareAndSwap"/>. It writes only when the record's revision is the
    /// expected one: the result is conflict for another revision and not found when the record is absent. The lifetime
    /// replaces the record's, so a sliding expiry is a write. A value above
    /// <see cref="CoordinationLimits.MaxValueLength"/> bytes throws <see cref="ArgumentException"/>. The provider reads
    /// the value before the returned task completes, so the caller may reuse its buffer once it does.
    /// </remarks>
    /// <param name="key">The key of the record.</param>
    /// <param name="expected">The revision the record must have.</param>
    /// <param name="value">The new value of the record.</param>
    /// <param name="lifetime">How long the record lasts from this write.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Written with the new revision, conflict, not found, or session ended.</returns>
    ValueTask<RecordWriteResult> CompareAndSwapAsync(
        CoordinationKey key,
        Revision expected,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes a record when its revision is the expected one.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordCompareAndDelete"/>. It deletes only when the record's revision is
    /// the expected one: the result is conflict for another revision and not found when the record is absent.
    /// </remarks>
    /// <param name="key">The key of the record.</param>
    /// <param name="expected">The revision the record must have.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>Deleted, conflict, or not found.</returns>
    ValueTask<RecordDeleteStatus> CompareAndDeleteAsync(
        CoordinationKey key,
        Revision expected,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Reads a record and deletes it.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordTake"/>. It reads and deletes the record in one atomic step, so one
    /// caller takes each record. The result is null when the key is absent.
    /// </remarks>
    /// <param name="key">The key of the record.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The record that was deleted, or null when it is absent.</returns>
    ValueTask<StoredRecord?> TakeAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Lists the records under a prefix.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordList"/>. It lists the records whose key the prefix is a prefix of, as
    /// <see cref="CoordinationKey.IsPrefixOf"/> decides, the prefix's own included, in an order the provider chooses.
    /// Each page is consistent on its own.
    /// </remarks>
    /// <param name="prefix">The prefix of the keys to list.</param>
    /// <param name="page">Which page to read.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The page of records, with the token of the next page when there is one.</returns>
    ValueTask<RecordPage> ListAsync(
        CoordinationKey prefix,
        RecordPageRequest page,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
