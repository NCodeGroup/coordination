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
    private static KeySegment WriterSegment { get; } = KeySegment.FromString("writer");

    // A reader holds the probe only while it writes its registration, so 30 seconds is far more than it needs.
    private static LeaseTerms ProbeTerms { get; } =
        LeaseTerms.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)));

    private ILeaseService Leases { get; } = leases;

    private IRecordService Records { get; } = records;

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

            var registration = RecipeKeys.Child(
                resource,
                KeySegment.FromInt64(probe.Grant.FencingToken.Value),
                nameof(resource)
            );

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
