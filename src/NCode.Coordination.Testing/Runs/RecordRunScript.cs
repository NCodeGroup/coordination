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
/// keys, using the last revision it saw for each as the one it expects.
/// </summary>
/// <param name="records">The record service to call.</param>
/// <param name="recorder">The recorder that records every call.</param>
/// <param name="keys">The keys of the records the run uses.</param>
internal sealed class RecordRunScript(
    IRecordService records,
    HistoryRecorder recorder,
    ImmutableArray<CoordinationKey> keys
)
{
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
        var partition = key.ToString();
        var value = string.Create(CultureInfo.InvariantCulture, $"{client}-{index}");
        var bytes = HistoryValues.Encode(value);
        var expected = Math.Max(1, Seen[slot]);

        switch ((client + index) % 5)
        {
            case 0:
                var create = new RecordCall(RecordCall.Create, value, 0);
                var created = await recorder.RecordAsync(
                    client,
                    partition,
                    create,
                    token =>
                        records.CreateAsync(key, bytes, RecordLifetime.Persistent, null, token),
                    cancellationToken
                );
                Remember(slot, created);
                break;

            case 1:
            case 3:
                var read = new RecordCall(RecordCall.Read, null, 0);
                var observed = await recorder.RecordAsync(
                    client,
                    partition,
                    read,
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
                var swapped = await recorder.RecordAsync(
                    client,
                    partition,
                    swap,
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
                    await recorder.RecordAsync(
                        client,
                        partition,
                        delete,
                        token =>
                            records.CompareAndDeleteAsync(key, new Revision(expected), null, token),
                        cancellationToken
                    );
                }
                else
                {
                    var take = new RecordCall(RecordCall.Take, null, 0);
                    await recorder.RecordAsync(
                        client,
                        partition,
                        take,
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
            var partition = key.ToString();
            var value = string.Create(CultureInfo.InvariantCulture, $"closing-{k}");
            var bytes = HistoryValues.Encode(value);

            var read = await recorder.RecordAsync(
                HistoryOperation.TestClient,
                partition,
                new RecordCall(RecordCall.Read, null, 0),
                token => records.ReadAsync(key, null, token),
                cancellationToken
            );
            Assert.Equal(HistoryOutcome.Returned, read.Outcome);

            var stored = read.Output as StoredRecord;
            var write = stored is null
                ? await recorder.RecordAsync(
                    HistoryOperation.TestClient,
                    partition,
                    new RecordCall(RecordCall.Create, value, 0),
                    token =>
                        records.CreateAsync(key, bytes, RecordLifetime.Persistent, null, token),
                    cancellationToken
                )
                : await recorder.RecordAsync(
                    HistoryOperation.TestClient,
                    partition,
                    new RecordCall(RecordCall.CompareAndSwap, value, stored.Revision.Value),
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
