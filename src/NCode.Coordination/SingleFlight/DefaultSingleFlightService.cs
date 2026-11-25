// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Recipes;

namespace NCode.Coordination.SingleFlight;

/// <summary>
/// Lets one caller fetch a value while the others wait, with a record for the value and a lease for the fetch.
/// </summary>
/// <param name="records">The record operations the value is stored with.</param>
/// <param name="leases">The lease operations the fetching caller's turn is built on.</param>
internal class DefaultSingleFlightService(IRecordService records, ILeaseService leases)
    : ISingleFlightService
{
    private static KeySegment FetchSegment { get; } = KeySegment.FromString("fetch");

    private IRecordService Records { get; } = records;

    private ILeaseService Leases { get; } = leases;

    /// <inheritdoc />
    public async ValueTask<StoredRecord> GetOrFetchAsync(
        CoordinationKey key,
        SingleFlightTerms terms,
        Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>>> fetch,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(fetch);

        var fetchKey = RecipeKeys.Child(key, FetchSegment, nameof(key));
        var fetchTerms = LeaseTerms.ForTimeToLive(terms.FetchTimeToLive);
        var valueLifetime = RecordLifetime.ForTimeToLive(terms.ValueTimeToLive);

        while (true)
        {
            var stored = await Records
                .ReadAsync(key, options, cancellationToken)
                .ConfigureAwait(false);

            if (stored is not null)
            {
                return stored;
            }

            var lease = await Leases
                .TryAcquireAsync(fetchKey, fetchTerms, options, cancellationToken)
                .ConfigureAwait(false);

            if (!lease.IsAcquired)
            {
                await Leases
                    .WaitForReleaseAsync(fetchKey, options, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            try
            {
                // Another caller may have stored the value between the first read and the lease.
                stored = await Records
                    .ReadAsync(key, options, cancellationToken)
                    .ConfigureAwait(false);

                if (stored is not null)
                {
                    return stored;
                }

                var value = await fetch(cancellationToken).ConfigureAwait(false);

                var written = await Records
                    .CreateAsync(key, value, valueLifetime, options, cancellationToken)
                    .ConfigureAwait(false);

                if (written.IsWritten)
                {
                    return new StoredRecord(key, value, written.Revision);
                }
            }
            finally
            {
                await Leases
                    .ReleaseAsync(lease.Grant, options, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }
}
