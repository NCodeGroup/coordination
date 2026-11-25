// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Globalization;
using NCode.Coordination.Testing.Histories;
using NCode.Coordination.Testing.Models;
using Xunit;

namespace NCode.Coordination.Testing.Runs;

/// <summary>
/// The steps of a concurrent run on records: each client creates, reads, swaps, deletes and takes the records of a few
/// keys, using the last revision it saw for each as the one it expects; with a list prefix, it records every call in
/// one partition and lists both keys in place of one of its reads.
/// </summary>
/// <param name="records">The record service to call.</param>
/// <param name="recorder">The recorder that records every call.</param>
/// <param name="keys">The keys of the records the run uses.</param>
/// <param name="listPrefix">
/// The prefix that lists every key of the run, or null for a run that keeps each key in a partition of its own and
/// never lists.
/// </param>
internal sealed class RecordRunScript(
    IRecordService records,
    HistoryRecorder recorder,
    ImmutableArray<CoordinationKey> keys,
    CoordinationKey? listPrefix = null
)
{
    /// <summary>
    /// The partition that holds every call of a run with a list prefix.
    /// </summary>
    public const string ListingPartition = "listing";

    /// <summary>
    /// The most records that a listing returns in a page.
    /// </summary>
    private const int ListLimit = 10;

    /// <summary>
    /// Gets the request for the first page of a listing.
    /// </summary>
    private static RecordPageRequest ListPage { get; } = new(ListLimit);

    /// <summary>
    /// Gets the last revision each client saw for each key, 0 when it saw none, indexed by the client and then the key.
    /// </summary>
    private long[] Seen { get; } = new long[ConcurrentRun.Clients * keys.Length];

    /// <summary>
    /// Runs one step of a client: a call chosen from the client and the index of the step.
    /// </summary>
    /// <param name="client">The client that runs the step.</param>
    /// <param name="index">The index of the step.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A task that completes when the step's call has been recorded.</returns>
    public async ValueTask StepAsync(int client, int index, CancellationToken cancellationToken)
    {
        var k = (client + index) % keys.Length;
        var key = keys[k];
        var slot = (client * keys.Length) + k;
        var partition = PartitionOf(key);
        var value = string.Create(CultureInfo.InvariantCulture, $"{client}-{index}");
        var bytes = HistoryValues.Encode(value);
        var expected = Math.Max(1, Seen[slot]);

        if ((client + index) % 5 == 3 && listPrefix is { } prefix)
        {
            await ListAsync(client, prefix, cancellationToken);
            return;
        }

        switch ((client + index) % 5)
        {
            case 0:
                var create = new RecordCall(RecordCall.Create, value, 0);
                var createInput = InputOf(k, create);
                var created = await recorder.RecordAsync(
                    client,
                    partition,
                    createInput,
                    token =>
                        records.CreateAsync(key, bytes, RecordLifetime.Persistent, null, token),
                    cancellationToken
                );
                Remember(slot, created);
                break;

            case 1:
            case 3:
                var read = new RecordCall(RecordCall.Read, null, 0);
                var readInput = InputOf(k, read);
                var observed = await recorder.RecordAsync(
                    client,
                    partition,
                    readInput,
                    token => records.ReadAsync(key, null, token),
                    cancellationToken
                );
                if (observed.Outcome == HistoryOutcome.Returned)
                {
                    Seen[slot] = (observed.Output as StoredRecord)?.Revision.Value ?? 0;
                }

                break;

            case 2:
                var swap = new RecordCall(RecordCall.CompareAndSwap, value, expected);
                var swapInput = InputOf(k, swap);
                var swapped = await recorder.RecordAsync(
                    client,
                    partition,
                    swapInput,
                    token =>
                        records.CompareAndSwapAsync(
                            key,
                            new Revision(expected),
                            bytes,
                            RecordLifetime.Persistent,
                            null,
                            token
                        ),
                    cancellationToken
                );
                Remember(slot, swapped);
                break;

            default:
                if (index % 2 == 0)
                {
                    var delete = new RecordCall(RecordCall.CompareAndDelete, null, expected);
                    var deleteInput = InputOf(k, delete);
                    await recorder.RecordAsync(
                        client,
                        partition,
                        deleteInput,
                        token =>
                            records.CompareAndDeleteAsync(key, new Revision(expected), null, token),
                        cancellationToken
                    );
                }
                else
                {
                    var take = new RecordCall(RecordCall.Take, null, 0);
                    var takeInput = InputOf(k, take);
                    await recorder.RecordAsync(
                        client,
                        partition,
                        takeInput,
                        token => records.TakeAsync(key, null, token),
                        cancellationToken
                    );
                }

                break;
        }
    }

    /// <summary>
    /// Writes a closing value to each key as the test itself, so that a history ends in a known state.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the writes.</param>
    /// <returns>The revision that each key's closing value was written at.</returns>
    public async ValueTask<ImmutableDictionary<CoordinationKey, Revision>> WriteClosingValuesAsync(
        CancellationToken cancellationToken
    )
    {
        var closing = ImmutableDictionary.CreateBuilder<CoordinationKey, Revision>();

        for (var k = 0; k < keys.Length; k++)
        {
            var key = keys[k];
            var partition = PartitionOf(key);
            var value = string.Create(CultureInfo.InvariantCulture, $"closing-{k}");
            var bytes = HistoryValues.Encode(value);

            var readCall = new RecordCall(RecordCall.Read, null, 0);
            var readInput = InputOf(k, readCall);
            var read = await recorder.RecordAsync(
                HistoryOperation.TestClient,
                partition,
                readInput,
                token => records.ReadAsync(key, null, token),
                cancellationToken
            );
            Assert.Equal(HistoryOutcome.Returned, read.Outcome);

            var stored = read.Output as StoredRecord;
            var createCall = new RecordCall(RecordCall.Create, value, 0);
            var createInput = InputOf(k, createCall);
            var swapCall = new RecordCall(
                RecordCall.CompareAndSwap,
                value,
                stored?.Revision.Value ?? 0
            );
            var swapInput = InputOf(k, swapCall);
            var write = stored is null
                ? await recorder.RecordAsync(
                    HistoryOperation.TestClient,
                    partition,
                    createInput,
                    token =>
                        records.CreateAsync(key, bytes, RecordLifetime.Persistent, null, token),
                    cancellationToken
                )
                : await recorder.RecordAsync(
                    HistoryOperation.TestClient,
                    partition,
                    swapInput,
                    token =>
                        records.CompareAndSwapAsync(
                            key,
                            stored.Revision,
                            bytes,
                            RecordLifetime.Persistent,
                            null,
                            token
                        ),
                    cancellationToken
                );

            var result = Assert.IsType<RecordWriteResult>(write.Output);
            Assert.True(result.IsWritten);
            closing[key] = result.Revision;
        }

        return closing.ToImmutable();
    }

    /// <summary>
    /// Gets the partition that a call on a key is recorded in.
    /// </summary>
    /// <param name="key">The key the call names.</param>
    /// <returns>The key's own partition, or <see cref="ListingPartition"/> when the run lists.</returns>
    private string PartitionOf(CoordinationKey key) =>
        listPrefix is null ? key.ToString() : ListingPartition;

    /// <summary>
    /// Gets the input that a call on a key is recorded with.
    /// </summary>
    /// <param name="k">The index of the key the call names.</param>
    /// <param name="call">The call.</param>
    /// <returns>The call itself, or the call keyed by <paramref name="k"/> when the run lists.</returns>
    private object InputOf(int k, RecordCall call) =>
        listPrefix is null ? call : new KeyedCall(k, call);

    /// <summary>
    /// Lists every key of the run and records the listing.
    /// </summary>
    /// <param name="client">The client that lists.</param>
    /// <param name="prefix">The prefix that lists every key of the run.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>A task that completes when the listing has been recorded.</returns>
    private async ValueTask ListAsync(
        int client,
        CoordinationKey prefix,
        CancellationToken cancellationToken
    )
    {
        var list = new RecordCall(RecordCall.List, null, 0);
        await recorder.RecordAsync(
            client,
            ListingPartition,
            list,
            token => records.ListAsync(prefix, ListPage, null, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Keeps the revision that a write returned as the last one the client saw.
    /// </summary>
    /// <param name="slot">The index of the client and key in <see cref="Seen"/>.</param>
    /// <param name="operation">The recorded write.</param>
    private void Remember(int slot, HistoryOperation operation)
    {
        if (operation.Output is RecordWriteResult { IsWritten: true } result)
        {
            Seen[slot] = result.Revision.Value;
        }
    }
}
