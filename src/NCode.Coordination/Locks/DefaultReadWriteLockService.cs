// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Recipes;

namespace NCode.Coordination.Locks;

/// <summary>
/// Locks resources for many readers or one writer, with a lease for the writer and a record for each reader.
/// </summary>
/// <param name="leases">The lease operations the writer's lock is built on.</param>
/// <param name="records">The record operations the readers' registrations are built on.</param>
/// <param name="watches">The record watches a writer waits on for the readers to leave.</param>
internal class DefaultReadWriteLockService(
    ILeaseService leases,
    IRecordService records,
    IRecordWatchService watches
) : IReadWriteLockService
{
    /// <summary>
    /// Gets the segment of the resource's key that names the writer's lease.
    /// </summary>
    private static KeySegment WriterSegment { get; } = KeySegment.FromString("writer");

    // A reader holds the probe only while it writes its registration, so 30 seconds is far more than it needs.
    /// <summary>
    /// Gets how long the lease that a reader holds while it writes its registration lasts.
    /// </summary>
    private static TimeSpan ProbeLength { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the time to live of the lease that a reader holds while it writes its registration.
    /// </summary>
    private static TimeToLive ProbeTimeToLive { get; } = new(ProbeLength);

    /// <summary>
    /// Gets the terms of the lease that a reader holds while it writes its registration.
    /// </summary>
    private static LeaseTerms ProbeTerms { get; } = LeaseTerms.ForTimeToLive(ProbeTimeToLive);

    /// <summary>
    /// Gets the lease operations that hold the writer's lock and the readers' probe.
    /// </summary>
    private ILeaseService Leases { get; } = leases;

    /// <summary>
    /// Gets the record operations that write, list and delete the readers' registrations.
    /// </summary>
    private IRecordService Records { get; } = records;

    /// <summary>
    /// Gets the record watches that report a reader leaving.
    /// </summary>
    private IRecordWatchService Watches { get; } = watches;

    /// <inheritdoc />
    public async ValueTask<ReadLockGrant?> AcquireReadAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(terms);

        var writer = RecipeKeys.Child(resource, WriterSegment, nameof(resource));
        var lifetime = terms.IsBoundToSession
            ? RecordLifetime.ForSession(terms.Session)
            : RecordLifetime.ForTimeToLive(terms.TimeToLive);

        while (true)
        {
            var probe = await Leases
                .TryAcquireAsync(writer, ProbeTerms, options, cancellationToken)
                .ConfigureAwait(false);

            if (!probe.IsAcquired)
            {
                await Leases
                    .WaitForReleaseAsync(writer, options, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var token = KeySegment.FromInt64(probe.Grant.FencingToken.Value);
            var registration = RecipeKeys.Child(resource, token, nameof(resource));

            RecordWriteResult written;
            try
            {
                written = await Records
                    .CreateAsync(
                        registration,
                        ReadOnlyMemory<byte>.Empty,
                        lifetime,
                        options,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch
            {
                await Leases
                    .ReleaseAsync(probe.Grant, options, CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }

            // Holding the probe while the registration is written keeps every writer out of that moment.
            var released = await Leases
                .ReleaseAsync(probe.Grant, options, CancellationToken.None)
                .ConfigureAwait(false);

            if (written.Status == RecordWriteStatus.SessionEnded)
            {
                return null;
            }

            if (!written.IsWritten)
            {
                continue;
            }

            if (!released)
            {
                // The probe outlived its time to live, so a writer may have listed the readers without this one.
                await Records
                    .CompareAndDeleteAsync(
                        registration,
                        written.Revision,
                        options,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                continue;
            }

            return new ReadLockGrant(
                resource,
                registration,
                written.Revision,
                probe.Grant.FencingToken
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseReadAsync(
        ReadLockGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);

        var status = await Records
            .CompareAndDeleteAsync(grant.Registration, grant.Revision, options, cancellationToken)
            .ConfigureAwait(false);

        return status == RecordDeleteStatus.Deleted;
    }

    /// <inheritdoc />
    public async ValueTask<LeaseGrant?> AcquireWriteAsync(
        CoordinationKey resource,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(terms);

        var writer = RecipeKeys.Child(resource, WriterSegment, nameof(resource));

        var grant = await AcquireWriterAsync(writer, terms, options, cancellationToken)
            .ConfigureAwait(false);

        if (grant is null)
        {
            return null;
        }

        try
        {
            await WaitForNoReadersAsync(resource, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await Leases.ReleaseAsync(grant, options, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return grant;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseWriteAsync(
        LeaseGrant grant,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(grant);

        return await Leases.ReleaseAsync(grant, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Acquires the writer's lease, waiting for its release while another writer holds it.
    /// </summary>
    /// <param name="writer">The key of the writer's lease.</param>
    /// <param name="terms">The terms of the lease.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The grant, or null when the session the terms name has ended.</returns>
    private async ValueTask<LeaseGrant?> AcquireWriterAsync(
        CoordinationKey writer,
        LeaseTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var result = await Leases
                .TryAcquireAsync(writer, terms, options, cancellationToken)
                .ConfigureAwait(false);

            switch (result.Status)
            {
                case LeaseAcquireStatus.Acquired:
                    return result.Grant;
                case LeaseAcquireStatus.SessionEnded:
                    return null;
                default:
                    await Leases
                        .WaitForReleaseAsync(writer, options, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>
    /// Waits until no reader has a registration under the resource.
    /// </summary>
    /// <param name="resource">The resource whose readers to wait for.</param>
    /// <param name="options">Options for this call, or null for none.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>A task that completes when the resource has no readers.</returns>
    /// <exception cref="InvalidOperationException">The watch ended without a failure.</exception>
    private async ValueTask WaitForNoReadersAsync(
        CoordinationKey resource,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        // A record at the resource itself is not a reader, so two records are enough to see one reader.
        var request = new RecordPageRequest(2);

        var page = await Records
            .ListAsync(resource, request, options, cancellationToken)
            .ConfigureAwait(false);

        if (!HasReader(page, resource))
        {
            return;
        }

        await foreach (
            var change in Watches
                .WatchPrefixAsync(resource, page.Revision, options, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            // A registration cannot be written while this writer holds the lease, so a write is not a departure.
            if (change.Kind == RecordChangeKind.Written)
            {
                continue;
            }

            page = await Records
                .ListAsync(resource, request, options, cancellationToken)
                .ConfigureAwait(false);

            if (!HasReader(page, resource))
            {
                return;
            }
        }

        throw new InvalidOperationException("The watch ended without a failure.");
    }

    /// <summary>
    /// Determines whether a page of records holds a reader's registration.
    /// </summary>
    /// <param name="page">The page of records listed under the resource.</param>
    /// <param name="resource">The resource whose readers to look for.</param>
    /// <returns>True when a record sits below the resource's own key; otherwise false.</returns>
    private static bool HasReader(RecordPage page, CoordinationKey resource)
    {
        foreach (var record in page.Records)
        {
            if (record.Key.Segments.Length > resource.Segments.Length)
            {
                return true;
            }
        }

        return false;
    }
}
