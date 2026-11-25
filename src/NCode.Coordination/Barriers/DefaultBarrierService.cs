// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Recipes;

namespace NCode.Coordination.Barriers;

/// <summary>
/// Holds each party at a barrier until every party has arrived, with a record for each arrival and a watch on them.
/// </summary>
/// <param name="records">The record operations the arrivals are built on.</param>
/// <param name="watches">The record watches a party waits on for the other arrivals.</param>
internal class DefaultBarrierService(IRecordService records, IRecordWatchService watches)
    : IBarrierService
{
    private IRecordService Records { get; } = records;

    private IRecordWatchService Watches { get; } = watches;

    /// <inheritdoc />
    public async ValueTask<bool> ArriveAndWaitAsync(
        CoordinationKey barrier,
        KeySegment participant,
        BarrierTerms terms,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(barrier);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(terms);

        var arrival = RecipeKeys.Child(barrier, participant, nameof(barrier));

        var written = await Records
            .CreateAsync(
                arrival,
                ReadOnlyMemory<byte>.Empty,
                terms.Lifetime,
                options,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (written.Status == RecordWriteStatus.SessionEnded)
        {
            return false;
        }

        // A conflict means this party already arrived, which is fine: it counts once.
        var arrived = new HashSet<CoordinationKey>();

        var page = await ListArrivalsAsync(barrier, arrived, options, cancellationToken)
            .ConfigureAwait(false);

        if (arrived.Count >= terms.Parties)
        {
            return true;
        }

        await foreach (
            var change in Watches
                .WatchPrefixAsync(barrier, page.Revision, options, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            if (change.IsResync)
            {
                await ListArrivalsAsync(barrier, arrived, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (change.Kind == RecordChangeKind.Written && !change.Key.Equals(barrier))
            {
                arrived.Add(change.Key);
            }

            // A party counts once it is seen arriving, even when its record later ends.
            if (arrived.Count >= terms.Parties)
            {
                return true;
            }
        }

        throw new InvalidOperationException("The watch ended without a failure.");
    }

    private async ValueTask<RecordPage> ListArrivalsAsync(
        CoordinationKey barrier,
        HashSet<CoordinationKey> arrived,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        var request = new RecordPageRequest(CoordinationLimits.MaxPageSize);

        var page = await Records
            .ListAsync(barrier, request, options, cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in page.Records)
        {
            if (!record.Key.Equals(barrier))
            {
                arrived.Add(record.Key);
            }
        }

        return page;
    }
}
