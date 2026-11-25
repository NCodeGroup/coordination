// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// Records whose calls fail while the connection is down.
/// </summary>
/// <param name="inner">The records to decorate.</param>
/// <param name="connection">The connection that a test drops.</param>
internal sealed class DroppingRecordService(IRecordService inner, DroppingConnection connection)
    : IRecordService
{
    /// <summary>
    /// Gets the records that this decorator calls over the connection.
    /// </summary>
    private IRecordService Inner { get; } = inner;

    /// <summary>
    /// Gets the connection that a test drops.
    /// </summary>
    private DroppingConnection Connection { get; } = connection;

    /// <inheritdoc />
    public ValueTask<RecordWriteResult> CreateAsync(
        CoordinationKey key,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) =>
        Connection.CallAsync(
            token => Inner.CreateAsync(key, value, lifetime, options, token),
            cancellationToken
        );

    /// <inheritdoc />
    public ValueTask<StoredRecord?> ReadAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) => Connection.CallAsync(token => Inner.ReadAsync(key, options, token), cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecordWriteResult> CompareAndSwapAsync(
        CoordinationKey key,
        Revision expected,
        ReadOnlyMemory<byte> value,
        RecordLifetime lifetime,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) =>
        Connection.CallAsync(
            token => Inner.CompareAndSwapAsync(key, expected, value, lifetime, options, token),
            cancellationToken
        );

    /// <inheritdoc />
    public ValueTask<RecordDeleteStatus> CompareAndDeleteAsync(
        CoordinationKey key,
        Revision expected,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) =>
        Connection.CallAsync(
            token => Inner.CompareAndDeleteAsync(key, expected, options, token),
            cancellationToken
        );

    /// <inheritdoc />
    public ValueTask<StoredRecord?> TakeAsync(
        CoordinationKey key,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) => Connection.CallAsync(token => Inner.TakeAsync(key, options, token), cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecordPage> ListAsync(
        CoordinationKey prefix,
        RecordPageRequest page,
        OperationOptions? options,
        CancellationToken cancellationToken
    ) =>
        Connection.CallAsync(
            token => Inner.ListAsync(prefix, page, options, token),
            cancellationToken
        );
}
