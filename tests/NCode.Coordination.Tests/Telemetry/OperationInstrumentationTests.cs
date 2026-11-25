// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using NCode.Coordination.Composition;

namespace NCode.Coordination.Telemetry.Tests;

public sealed class OperationInstrumentationTests
{
    private const string DurationName = "ncode.coordination.operation.duration";

    private static BoundService<string> Target { get; } =
        new("memory", Primitives.Occurrence, "service");

    private static Operation Operation { get; } = Operations.OccurrenceTryRecord;

    #region MeasureAsync Tests

    [Fact]
    public async Task MeasureAsync_WhenTheCallSucceeds_RecordsSuccessWithTheTags()
    {
        using var fixture = new Fixture();

        var result = await fixture.Instrumentation.MeasureAsync(Target, Operation, service =>
            ValueTask.FromResult(service + "!")
        );

        Assert.Equal("service!", result);
        var measurement = Assert.Single(fixture.Collector.GetMeasurementSnapshot());
        Assert.True(measurement.Value >= 0);
        Assert.Equal("memory", measurement.Tags["ncode.coordination.provider"]);
        Assert.Equal("occurrence", measurement.Tags["ncode.coordination.primitive"]);
        Assert.Equal("occurrence.try-record", measurement.Tags["ncode.coordination.operation"]);
        Assert.Equal("success", measurement.Tags["ncode.coordination.outcome"]);
        Assert.False(measurement.Tags.ContainsKey("error.type"));
    }

    [Fact]
    public async Task MeasureAsync_WhenTheCallSucceeds_RecordsASpan()
    {
        using var fixture = new Fixture();

        await fixture.Instrumentation.MeasureAsync(Target, Operation, service =>
            ValueTask.FromResult(service)
        );

        var span = Assert.Single(fixture.Spans);
        Assert.Equal("ncode.coordination.operation", span.OperationName);
        Assert.Equal("occurrence.try-record", span.DisplayName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("memory", span.GetTagItem("ncode.coordination.provider"));
        Assert.Equal("occurrence", span.GetTagItem("ncode.coordination.primitive"));
        Assert.Equal("occurrence.try-record", span.GetTagItem("ncode.coordination.operation"));
        Assert.Equal("success", span.GetTagItem("ncode.coordination.outcome"));
        Assert.Null(span.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task MeasureAsync_WhenACoordinationExceptionIsThrown_RecordsFailureWithItsCode()
    {
        using var fixture = new Fixture();
        var thrown = new CoordinationException(
            CoordinationErrorCodes.Unsupported,
            outcomeUnknown: false,
            "down"
        );

        var caught = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await fixture.Instrumentation.MeasureAsync(Target, Operation, _ =>
                ValueTask.FromException<string>(thrown)
            )
        );

        Assert.Same(thrown, caught);
        var measurement = Assert.Single(fixture.Collector.GetMeasurementSnapshot());
        Assert.Equal("failure", measurement.Tags["ncode.coordination.outcome"]);
        Assert.Equal(thrown.Code, measurement.Tags["error.type"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Null(span.StatusDescription);
        Assert.Equal("failure", span.GetTagItem("ncode.coordination.outcome"));
        Assert.Equal(thrown.Code, span.GetTagItem("error.type"));
    }

    [Fact]
    public async Task MeasureAsync_WhenAnotherExceptionIsThrown_RecordsItsTypeName()
    {
        using var fixture = new Fixture();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Instrumentation.MeasureAsync(Target, Operation, _ =>
                ValueTask.FromException<string>(new InvalidOperationException("secret"))
            )
        );

        var measurement = Assert.Single(fixture.Collector.GetMeasurementSnapshot());
        Assert.Equal("failure", measurement.Tags["ncode.coordination.outcome"]);
        Assert.Equal("System.InvalidOperationException", measurement.Tags["error.type"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("System.InvalidOperationException", span.GetTagItem("error.type"));
    }

    [Fact]
    public async Task MeasureAsync_WhenCanceled_RecordsCanceledWithoutErrorType()
    {
        using var fixture = new Fixture();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await fixture.Instrumentation.MeasureAsync(Target, Operation, _ =>
                ValueTask.FromException<string>(new OperationCanceledException())
            )
        );

        var measurement = Assert.Single(fixture.Collector.GetMeasurementSnapshot());
        Assert.Equal("canceled", measurement.Tags["ncode.coordination.outcome"]);
        Assert.False(measurement.Tags.ContainsKey("error.type"));
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        Assert.Equal("canceled", span.GetTagItem("ncode.coordination.outcome"));
        Assert.Null(span.GetTagItem("error.type"));
    }

    [Fact]
    public async Task MeasureAsync_WhenTheCallReturnsNoValue_RecordsSuccess()
    {
        using var fixture = new Fixture();

        await fixture.Instrumentation.MeasureAsync(Target, Operation, _ => ValueTask.CompletedTask);

        var measurement = Assert.Single(fixture.Collector.GetMeasurementSnapshot());
        Assert.Equal("success", measurement.Tags["ncode.coordination.outcome"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal("success", span.GetTagItem("ncode.coordination.outcome"));
    }

    [Fact]
    public async Task MeasureAsync_RecordsNoKeyOrValue()
    {
        using var fixture = new Fixture();

        await fixture.Instrumentation.MeasureAsync(Target, Operation, service =>
            ValueTask.FromResult(service)
        );

        var measurement = Assert.Single(fixture.Collector.GetMeasurementSnapshot());
        Assert.Equal(
            [
                "ncode.coordination.operation",
                "ncode.coordination.outcome",
                "ncode.coordination.primitive",
                "ncode.coordination.provider",
            ],
            measurement.Tags.Keys.Order(StringComparer.Ordinal)
        );
    }

    #endregion

    private sealed class Fixture : IDisposable
    {
        private ServiceProvider Services { get; }

        private Activity Parent { get; }

        private ActivityListener Listener { get; }

        public Fixture()
        {
            Services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            var meterFactory = Services.GetRequiredService<IMeterFactory>();
            Collector = new MetricCollector<double>(
                meterFactory,
                CoordinationTelemetry.MeterName,
                DurationName
            );
            Instrumentation = new OperationInstrumentation(meterFactory, TimeProvider.System);

            Parent = new Activity("test").Start();
            var parentTraceId = Parent.TraceId;
            Listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == CoordinationTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId == parentTraceId)
                    {
                        Spans.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(Listener);
        }

        public MetricCollector<double> Collector { get; }

        public OperationInstrumentation Instrumentation { get; }

        public List<Activity> Spans { get; } = [];

        public void Dispose()
        {
            Listener.Dispose();
            Parent.Stop();
            Parent.Dispose();
            Services.Dispose();
        }
    }
}
