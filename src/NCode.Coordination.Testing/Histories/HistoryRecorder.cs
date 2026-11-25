// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Testing.Histories;

/// <summary>
/// Records every call that concurrent clients make and every return, stamping each on one counter that increments
/// atomically, so no two stamps tie.
/// </summary>
/// <param name="callTimeout">How long a call may take before it is recorded as indeterminate.</param>
internal sealed class HistoryRecorder(TimeSpan callTimeout)
{
    /// <summary>
    /// The counter that <see cref="Stamp"/> increments atomically, the history's clock.
    /// </summary>
    private long _clock;

    /// <summary>
    /// Creates a recorder that gives a call the time that <see cref="ContractStreams.Timeout"/> allows.
    /// </summary>
    public HistoryRecorder()
        : this(ContractStreams.Timeout) { }

    /// <summary>
    /// Gets how long a call may take before it is recorded as indeterminate.
    /// </summary>
    public TimeSpan CallTimeout { get; } = callTimeout;

    /// <summary>
    /// Gets the lock that guards <see cref="Operations"/>.
    /// </summary>
    private Lock Gate { get; } = new();

    /// <summary>
    /// Gets the operations recorded so far, in the order they were added.
    /// </summary>
    private List<HistoryOperation> Operations { get; } = [];

    /// <summary>
    /// Takes the next stamp of the history's clock.
    /// </summary>
    /// <returns>A stamp greater than every stamp taken before.</returns>
    public long Stamp() => Interlocked.Increment(ref _clock);

    /// <summary>
    /// Makes a call that returns a result and records it.
    /// </summary>
    /// <remarks>
    /// A <see cref="CoordinationException"/> that says the call took no effect is recorded as failed. Another
    /// <see cref="CoordinationException"/>, a cancellation that the run's token did not request, and a call that does not
    /// end within <see cref="CallTimeout"/> are recorded as indeterminate, since the call may have applied. Any other
    /// exception, and a cancellation that the run's token requested, propagates and records nothing.
    /// </remarks>
    /// <typeparam name="TOutput">The type of the call's result.</typeparam>
    /// <param name="client">The client that makes the call.</param>
    /// <param name="partition">The key whose history the call belongs to.</param>
    /// <param name="input">The call: the operation and its arguments.</param>
    /// <param name="call">The call to make, given a token that also cancels when the call times out.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The operation that was recorded.</returns>
    public async ValueTask<HistoryOperation> RecordAsync<TOutput>(
        int client,
        string partition,
        object input,
        Func<CancellationToken, ValueTask<TOutput>> call,
        CancellationToken cancellationToken
    )
    {
        var invoked = Stamp();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CallTimeout);

        try
        {
            var output = await call(timeout.Token);

            return Add(client, partition, input, invoked, HistoryOutcome.Returned, output);
        }
        catch (Exception exception)
            when (exception is CoordinationException
                || (
                    exception is OperationCanceledException
                    && !cancellationToken.IsCancellationRequested
                )
            )
        {
            var outcome = exception is CoordinationException { OutcomeUnknown: false }
                ? HistoryOutcome.Failed
                : HistoryOutcome.Indeterminate;

            return Add(client, partition, input, invoked, outcome, exception);
        }
    }

    /// <summary>
    /// Makes a call that returns no result and records it.
    /// </summary>
    /// <param name="client">The client that makes the call.</param>
    /// <param name="partition">The key whose history the call belongs to.</param>
    /// <param name="input">The call: the operation and its arguments.</param>
    /// <param name="call">The call to make, given a token that also cancels when the call times out.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The operation that was recorded.</returns>
    public ValueTask<HistoryOperation> RecordAsync(
        int client,
        string partition,
        object input,
        Func<CancellationToken, ValueTask> call,
        CancellationToken cancellationToken
    ) =>
        RecordAsync<object?>(
            client,
            partition,
            input,
            async token =>
            {
                await call(token);
                return (object?)null;
            },
            cancellationToken
        );

    /// <summary>
    /// Gets the operations recorded so far.
    /// </summary>
    /// <returns>The operations, ordered by the stamp at which each was invoked.</returns>
    public ImmutableArray<HistoryOperation> ToHistory()
    {
        lock (Gate)
        {
            return [.. Operations.OrderBy(operation => operation.Invoked)];
        }
    }

    /// <summary>
    /// Records a call that has ended, stamping its return unless it is indeterminate.
    /// </summary>
    /// <param name="client">The client that made the call.</param>
    /// <param name="partition">The key whose history the call belongs to.</param>
    /// <param name="input">The call: the operation and its arguments.</param>
    /// <param name="invoked">The stamp taken when the call began.</param>
    /// <param name="outcome">How the call ended.</param>
    /// <param name="output">The result of the call, or the exception that ended it.</param>
    /// <returns>The operation that was recorded.</returns>
    private HistoryOperation Add(
        int client,
        string partition,
        object input,
        long invoked,
        HistoryOutcome outcome,
        object? output
    )
    {
        var responded = outcome == HistoryOutcome.Indeterminate ? long.MaxValue : Stamp();
        var operation = new HistoryOperation(
            client,
            partition,
            input,
            invoked,
            responded,
            outcome,
            output
        );

        lock (Gate)
        {
            Operations.Add(operation);
        }

        return operation;
    }
}
