// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination;

/// <summary>
/// The operations of record watches: the changes to a record or to the records under a prefix, in revision order.
/// </summary>
/// <remarks>
/// An operation returns each expected outcome in its result. It throws <see cref="CoordinationException"/> when the provider fails, with <see cref="CoordinationException.OutcomeUnknown"/> true when the operation may have taken effect; <see cref="OperationCanceledException"/> when the token is cancelled, after which a write's outcome is unknown; and <see cref="ArgumentException"/> or <see cref="ArgumentNullException"/> for an invalid argument. A provider refuses an extension in the options that it does not honor with the code <see cref="CoordinationErrorCodes.Unsupported"/>.
/// </remarks>
public interface IRecordWatchService
{
    /// <summary>
    /// Watches the changes to one record.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordWatchKey"/>. The stream starts with a resync, unless the provider
    /// keeps the history after <paramref name="after"/>, whose changes it then streams. It resyncs again whenever a
    /// change may have been missed, such as after a reconnect. A key's intermediate values may be skipped, but once
    /// the subscriber applies every change up to a revision, its view equals the records at that revision. The stream
    /// ends only when it is cancelled or when the provider fails.
    /// </remarks>
    /// <param name="key">The key of the record to watch.</param>
    /// <param name="after">The revision after which the subscriber wants the changes, or null for none.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The changes to the record, in revision order.</returns>
    IAsyncEnumerable<RecordChange> WatchKeyAsync(
        CoordinationKey key,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Watches the changes to the records under a prefix.
    /// </summary>
    /// <remarks>
    /// The operation <see cref="Operations.RecordWatchPrefix"/>. The stream starts with a resync, unless the provider
    /// keeps the history after <paramref name="after"/>, whose changes it then streams. It resyncs again whenever a
    /// change may have been missed, such as after a reconnect. A key's intermediate values may be skipped, but once
    /// the subscriber applies every change up to a revision, its view equals the records at that revision. The stream
    /// ends only when it is cancelled or when the provider fails.
    /// </remarks>
    /// <param name="prefix">The prefix of the keys to watch.</param>
    /// <param name="after">The revision after which the subscriber wants the changes, or null for none.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The changes to the records, in revision order.</returns>
    IAsyncEnumerable<RecordChange> WatchPrefixAsync(
        CoordinationKey prefix,
        Revision? after,
        OperationOptions? options,
        CancellationToken cancellationToken
    );
}
