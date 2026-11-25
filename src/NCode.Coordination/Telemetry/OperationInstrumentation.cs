// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    private const string SpanName = "ncode.coordination.operation";
    private const string ProviderTag = "ncode.coordination.provider";
    private const string PrimitiveTag = "ncode.coordination.primitive";
    private const string OperationTag = "ncode.coordination.operation";
    private const string OutcomeTag = "ncode.coordination.outcome";
    private const string ErrorTypeTag = "error.type";
    private const string SuccessOutcome = "success";
    private const string FailureOutcome = "failure";
    private const string CanceledOutcome = "canceled";

    /// <summary>
    /// Gets the activity source that records the operations' spans.
    /// </summary>
    internal static ActivitySource Source { get; } = new(CoordinationTelemetry.ActivitySourceName);

    private Histogram<double> Duration { get; } = CreateDuration(meterFactory);

    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <summary>
    /// Calls a provider's operation that returns a value, then traces and measures it.
    /// </summary>
    /// <typeparam name="TService">The primitive's interface.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="target">The bound provider's implementation.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="call">Calls the operation on the implementation.</param>
    /// <returns>The operation's result.</returns>
    public async ValueTask<TResult> MeasureAsync<TService, TResult>(
        BoundService<TService> target,
        Operation operation,
        Func<TService, ValueTask<TResult>> call
    )
        where TService : class
    {
        var measurement = Start(target, operation);
        try
        {
            var result = await call(target.Service).ConfigureAwait(false);
            Complete(measurement, null);
            return result;
        }
        catch (Exception exception)
        {
            Complete(measurement, exception);
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
    /// <returns>A task that completes when the operation does.</returns>
    public async ValueTask MeasureAsync<TService>(
        BoundService<TService> target,
        Operation operation,
        Func<TService, ValueTask> call
    )
        where TService : class
    {
        var measurement = Start(target, operation);
        try
        {
            await call(target.Service).ConfigureAwait(false);
            Complete(measurement, null);
        }
        catch (Exception exception)
        {
            Complete(measurement, exception);
            throw;
        }
    }

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

    private void Complete(OperationMeasurement measurement, Exception? exception)
    {
        string? errorType = null;
        string outcome;
        switch (exception)
        {
            case null:
                outcome = SuccessOutcome;
                break;
            case OperationCanceledException:
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
