// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using NCode.Coordination.Composition;

namespace NCode.Coordination.Telemetry;

/// <summary>
/// Traces and measures each operation: a span and a duration, tagged with the provider, the primitive, the operation
/// and the outcome, and never with a key, a segment or a value.
/// </summary>
/// <param name="meterFactory">Creates the meter that holds the duration histogram.</param>
/// <param name="timeProvider">Reads the clock that times each operation.</param>
internal class OperationInstrumentation(IMeterFactory meterFactory, TimeProvider timeProvider)
{
    /// <summary>
    /// The name of the span that traces each operation.
    /// </summary>
    private const string SpanName = "ncode.coordination.operation";

    /// <summary>
    /// The attribute that names the provider that served the operation.
    /// </summary>
    private const string ProviderTag = "ncode.coordination.provider";

    /// <summary>
    /// The attribute that names the primitive the operation belongs to.
    /// </summary>
    private const string PrimitiveTag = "ncode.coordination.primitive";

    /// <summary>
    /// The attribute that names the operation.
    /// </summary>
    private const string OperationTag = "ncode.coordination.operation";

    /// <summary>
    /// The attribute that holds the operation's outcome.
    /// </summary>
    private const string OutcomeTag = "ncode.coordination.outcome";

    /// <summary>
    /// The attribute that holds the error type of a failed operation.
    /// </summary>
    private const string ErrorTypeTag = "error.type";

    /// <summary>
    /// The outcome value of an operation that completed.
    /// </summary>
    private const string SuccessOutcome = "success";

    /// <summary>
    /// The outcome value of an operation that threw an exception other than a cancellation the caller requested.
    /// </summary>
    private const string FailureOutcome = "failure";

    /// <summary>
    /// The outcome value of an operation that the caller's token canceled.
    /// </summary>
    private const string CanceledOutcome = "canceled";

    /// <summary>
    /// Gets the activity source that records the operations' spans.
    /// </summary>
    internal static ActivitySource Source { get; } = new(CoordinationTelemetry.ActivitySourceName);

    /// <summary>
    /// Creates the histogram that records each operation's duration.
    /// </summary>
    /// <param name="meterFactory">Creates the meter that holds the histogram.</param>
    /// <returns>The duration histogram, in seconds.</returns>
    private static Histogram<double> CreateDuration(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(CoordinationTelemetry.MeterName);
        return meter.CreateHistogram<double>(
            "ncode.coordination.operation.duration",
            "s",
            "The duration of each coordination operation.",
            tags: null,
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries =
                [
                    0.0005,
                    0.001,
                    0.0025,
                    0.005,
                    0.01,
                    0.025,
                    0.05,
                    0.1,
                    0.25,
                    0.5,
                    1,
                    2.5,
                    5,
                    10,
                ],
            }
        );
    }

    /// <summary>
    /// Gets the histogram that records each operation's duration.
    /// </summary>
    private Histogram<double> Duration { get; } = CreateDuration(meterFactory);

    /// <summary>
    /// Gets the clock that times each operation.
    /// </summary>
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    /// Calls a provider's operation that returns a value, then traces and measures it.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="target">The bound provider's implementation.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="call">Calls the operation on the implementation.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The operation's result.</returns>
    public async ValueTask<TResult> MeasureAsync<TService, TResult>(
        BoundService<TService> target,
        Operation operation,
        Func<TService, CancellationToken, ValueTask<TResult>> call,
        CancellationToken cancellationToken
    )
        where TService : class
    {
        var measurement = Start(target, operation);
        try
        {
            var result = await call(target.Service, cancellationToken).ConfigureAwait(false);
            Complete(measurement, null, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            Complete(measurement, exception, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Calls a provider's operation that returns no value, then traces and measures it.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <param name="target">The bound provider's implementation.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="call">Calls the operation on the implementation.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the operation does.</returns>
    public async ValueTask MeasureAsync<TService>(
        BoundService<TService> target,
        Operation operation,
        Func<TService, CancellationToken, ValueTask> call,
        CancellationToken cancellationToken
    )
        where TService : class
    {
        var measurement = Start(target, operation);
        try
        {
            await call(target.Service, cancellationToken).ConfigureAwait(false);
            Complete(measurement, null, cancellationToken);
        }
        catch (Exception exception)
        {
            Complete(measurement, exception, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Opens a provider's stream, then traces and measures it from its first read to its end. A consumer that stops
    /// early counts as a success.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <typeparam name="TItem">The type of each item.</typeparam>
    /// <param name="target">The bound provider's implementation.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="open">Opens the stream on the implementation.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>The stream's items.</returns>
    public async IAsyncEnumerable<TItem> MeasureStreamAsync<TService, TItem>(
        BoundService<TService> target,
        Operation operation,
        Func<TService, CancellationToken, IAsyncEnumerable<TItem>> open,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
        where TService : class
    {
        var measurement = Start(target, operation);
        Exception? failure = null;
        IAsyncEnumerator<TItem>? enumerator = null;
        try
        {
            while (true)
            {
                TItem item;
                try
                {
                    enumerator ??= open(target.Service, cancellationToken)
                        .GetAsyncEnumerator(cancellationToken);
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }

                    item = enumerator.Current;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    throw;
                }

                yield return item;
            }
        }
        finally
        {
            try
            {
                if (enumerator is not null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Complete(measurement, failure, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Starts an operation's span and timer.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <param name="target">The bound provider's implementation.</param>
    /// <param name="operation">The operation.</param>
    /// <returns>The measurement to complete when the operation ends.</returns>
    private OperationMeasurement Start<TService>(BoundService<TService> target, Operation operation)
        where TService : class
    {
        var activity = Source.StartActivity(SpanName, ActivityKind.Client);
        if (activity is not null)
        {
            activity.DisplayName = operation.Name;
            activity.SetTag(ProviderTag, target.Provider);
            activity.SetTag(PrimitiveTag, target.Primitive.Name);
            activity.SetTag(OperationTag, operation.Name);
        }

        var started = TimeProvider.GetTimestamp();
        return new OperationMeasurement(
            target.Provider,
            target.Primitive,
            operation,
            activity,
            started
        );
    }

    /// <summary>
    /// Ends an operation: records its duration and closes its span, tagged with the outcome and any error type.
    /// </summary>
    /// <param name="measurement">The measurement that <c>Start</c> returned.</param>
    /// <param name="exception">The exception the operation ended with, or null for a success.</param>
    /// <param name="cancellationToken">The caller's token; a cancellation counts as canceled only when it is canceled.</param>
    private void Complete(
        OperationMeasurement measurement,
        Exception? exception,
        CancellationToken cancellationToken
    )
    {
        string? errorType = null;
        string outcome;
        switch (exception)
        {
            case null:
                outcome = SuccessOutcome;
                break;
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                outcome = CanceledOutcome;
                break;
            case CoordinationException coordinationException:
                outcome = FailureOutcome;
                errorType = coordinationException.Code;
                break;
            default:
                outcome = FailureOutcome;
                var type = exception.GetType();
                errorType = type.FullName ?? type.Name;
                break;
        }

        var tags = new TagList
        {
            { ProviderTag, measurement.Provider },
            { PrimitiveTag, measurement.Primitive.Name },
            { OperationTag, measurement.Operation.Name },
            { OutcomeTag, outcome },
        };
        if (errorType is not null)
        {
            tags.Add(ErrorTypeTag, errorType);
        }

        var elapsed = TimeProvider.GetElapsedTime(measurement.Started);
        Duration.Record(elapsed.TotalSeconds, tags);

        var activity = measurement.Activity;
        if (activity is null)
        {
            return;
        }

        activity.SetTag(OutcomeTag, outcome);
        if (errorType is not null)
        {
            activity.SetTag(ErrorTypeTag, errorType);
            activity.SetStatus(ActivityStatusCode.Error);
        }

        activity.Dispose();
    }
}
