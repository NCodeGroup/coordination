// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultOccurrenceServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region TryRecordAsync Tests

    [Fact]
    public async Task TryRecordAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ttl = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(ttl);
        var mock = Mocks.Create<IOccurrenceService>();
        mock.Setup(x => x.TryRecordAsync(TestComposition.Key, timeToLive, null, cancellationToken))
            .ReturnsAsync(OccurrenceStatus.Duplicate)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Occurrence, mock.Object);
        var service = provider.GetRequiredService<IOccurrenceService>();

        var result = await service.TryRecordAsync(
            TestComposition.Key,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.Equal(OccurrenceStatus.Duplicate, result);
    }

    [Fact]
    public async Task TryRecordAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ttl = TimeSpan.FromMinutes(1);
        var timeToLive = new TimeToLive(ttl);
        var mock = Mocks.Create<IOccurrenceService>();
        await using var provider = TestComposition.BuildElsewhere(
            Primitives.Occurrence,
            mock.Object
        );
        var service = provider.GetRequiredService<IOccurrenceService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryRecordAsync(TestComposition.Key, timeToLive, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion
}
