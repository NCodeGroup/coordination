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
        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.True(measurement.Value >= 0);
        Assert.Equal("memory", measurement.Tags["ncode.coordination.provider"]);
        Assert.Equal("occurrence", measurement.Tags["ncode.coordination.primitive"]);
        Assert.Equal("occurrence.try-record", measurement.Tags["ncode.coordination.operation"]);
        Assert.Equal("success", measurement.Tags["ncode.coordination.outcome"]);
        var hasErrorType = measurement.Tags.ContainsKey("error.type");
        Assert.False(hasErrorType);
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
        var provider = span.GetTagItem("ncode.coordination.provider");
        Assert.Equal("memory", provider);
        var primitive = span.GetTagItem("ncode.coordination.primitive");
        Assert.Equal("occurrence", primitive);
        var operation = span.GetTagItem("ncode.coordination.operation");
        Assert.Equal("occurrence.try-record", operation);
        var outcome = span.GetTagItem("ncode.coordination.outcome");
        Assert.Equal("success", outcome);
        var errorType = span.GetTagItem("error.type");
        Assert.Null(errorType);
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
        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("failure", measurement.Tags["ncode.coordination.outcome"]);
        Assert.Equal(thrown.Code, measurement.Tags["error.type"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Null(span.StatusDescription);
        var outcome = span.GetTagItem("ncode.coordination.outcome");
        Assert.Equal("failure", outcome);
        var errorType = span.GetTagItem("error.type");
        Assert.Equal(thrown.Code, errorType);
    }

    [Fact]
    public async Task MeasureAsync_WhenAnotherExceptionIsThrown_RecordsItsTypeName()
    {
        using var fixture = new Fixture();

        var thrown = new InvalidOperationException("secret");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Instrumentation.MeasureAsync(Target, Operation, _ =>
                ValueTask.FromException<string>(thrown)
            )
        );

        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("failure", measurement.Tags["ncode.coordination.outcome"]);
        Assert.Equal("System.InvalidOperationException", measurement.Tags["error.type"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        var errorType = span.GetTagItem("error.type");
        Assert.Equal("System.InvalidOperationException", errorType);
    }

    [Fact]
    public async Task MeasureAsync_WhenCanceled_RecordsCanceledWithoutErrorType()
    {
        using var fixture = new Fixture();

        var thrown = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await fixture.Instrumentation.MeasureAsync(Target, Operation, _ =>
                ValueTask.FromException<string>(thrown)
            )
        );

        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("canceled", measurement.Tags["ncode.coordination.outcome"]);
        var hasErrorType = measurement.Tags.ContainsKey("error.type");
        Assert.False(hasErrorType);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        var outcome = span.GetTagItem("ncode.coordination.outcome");
        Assert.Equal("canceled", outcome);
        var errorType = span.GetTagItem("error.type");
        Assert.Null(errorType);
    }

    [Fact]
    public async Task MeasureAsync_WhenTheCallReturnsNoValue_RecordsSuccess()
    {
        using var fixture = new Fixture();

        await fixture.Instrumentation.MeasureAsync(Target, Operation, _ => ValueTask.CompletedTask);

        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("success", measurement.Tags["ncode.coordination.outcome"]);
        var span = Assert.Single(fixture.Spans);
        var outcome = span.GetTagItem("ncode.coordination.outcome");
        Assert.Equal("success", outcome);
    }

    [Fact]
    public async Task MeasureAsync_RecordsNoKeyOrValue()
    {
        using var fixture = new Fixture();

        await fixture.Instrumentation.MeasureAsync(Target, Operation, service =>
            ValueTask.FromResult(service)
        );

        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        var keys = measurement.Tags.Keys.Order(StringComparer.Ordinal);
        Assert.Equal(
            [
                "ncode.coordination.operation",
                "ncode.coordination.outcome",
                "ncode.coordination.primitive",
                "ncode.coordination.provider",
            ],
            keys
        );
    }

    #endregion

    #region MeasureStream Tests

    [Fact]
    public async Task MeasureStream_WhenTheStreamEnds_YieldsEveryItemAndRecordsSuccess()
    {
        using var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        List<string> items = [];

        await foreach (
            var item in fixture.Instrumentation.MeasureStream(
                Target,
                Operation,
                _ => Items(["a", "b"]),
                cancellationToken
            )
        )
        {
            items.Add(item);
        }

        Assert.Equal(["a", "b"], items);
        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("success", measurement.Tags["ncode.coordination.outcome"]);
        var hasErrorType = measurement.Tags.ContainsKey("error.type");
        Assert.False(hasErrorType);
        var span = Assert.Single(fixture.Spans);
        var outcome = span.GetTagItem("ncode.coordination.outcome");
        Assert.Equal("success", outcome);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task MeasureStream_WhenTheConsumerStopsEarly_RecordsSuccess()
    {
        using var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        await foreach (
            var item in fixture.Instrumentation.MeasureStream(
                Target,
                Operation,
                _ => Items(["a", "b"]),
                cancellationToken
            )
        )
        {
            Assert.Equal("a", item);
            break;
        }

        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("success", measurement.Tags["ncode.coordination.outcome"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task MeasureStream_WhenTheStreamFails_RecordsFailureAndRethrows()
    {
        using var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        var thrown = new CoordinationException(
            CoordinationErrorCodes.Unsupported,
            outcomeUnknown: false,
            "down"
        );
        List<string> items = [];

        var caught = await Assert.ThrowsAsync<CoordinationException>(async () =>
        {
            await foreach (
                var item in fixture.Instrumentation.MeasureStream(
                    Target,
                    Operation,
                    _ => Failing(thrown),
                    cancellationToken
                )
            )
            {
                items.Add(item);
            }
        });

        Assert.Same(thrown, caught);
        Assert.Equal(["a"], items);
        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("failure", measurement.Tags["ncode.coordination.outcome"]);
        Assert.Equal(thrown.Code, measurement.Tags["error.type"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        var errorType = span.GetTagItem("error.type");
        Assert.Equal(thrown.Code, errorType);
    }

    [Fact]
    public async Task MeasureStream_WhenOpeningFails_RecordsFailure()
    {
        using var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (
                var item in fixture.Instrumentation.MeasureStream<string, string>(
                    Target,
                    Operation,
                    _ => throw new InvalidOperationException("secret"),
                    cancellationToken
                )
            )
            {
                Assert.Fail($"The stream yielded {item}.");
            }
        });

        var measurements = fixture.Collector.GetMeasurementSnapshot();
        var measurement = Assert.Single(measurements);
        Assert.Equal("failure", measurement.Tags["ncode.coordination.outcome"]);
        Assert.Equal("System.InvalidOperationException", measurement.Tags["error.type"]);
        var span = Assert.Single(fixture.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public void MeasureStream_BeforeTheFirstRead_RecordsNothing()
    {
        using var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        _ = fixture.Instrumentation.MeasureStream(
            Target,
            Operation,
            _ => Items(["a"]),
            cancellationToken
        );

        var measurements = fixture.Collector.GetMeasurementSnapshot();
        Assert.Empty(measurements);
        Assert.Empty(fixture.Spans);
    }

    #endregion

    private static async IAsyncEnumerable<string> Items(IReadOnlyList<string> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<string> Failing(Exception exception)
    {
        await Task.Yield();
        yield return "a";
        throw exception;
    }

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
