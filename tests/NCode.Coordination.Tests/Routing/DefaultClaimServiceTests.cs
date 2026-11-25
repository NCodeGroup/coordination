// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultClaimServiceTests : IDisposable
{
    private static TimeWindow Window()
    {
        var start = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
        var length = TimeSpan.FromHours(1);
        return new TimeWindow(start, length);
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = Window();
        var ttl = TimeSpan.FromHours(2);
        var timeToLive = new TimeToLive(ttl);
        var mock = Mocks.Create<IClaimService>();
        mock.Setup(x =>
                x.TryClaimAsync(TestComposition.Key, window, timeToLive, null, cancellationToken)
            )
            .ReturnsAsync(ClaimStatus.Claimed)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Claim, mock.Object);
        var service = provider.GetRequiredService<IClaimService>();

        var result = await service.TryClaimAsync(
            TestComposition.Key,
            window,
            timeToLive,
            null,
            cancellationToken
        );

        Assert.Equal(ClaimStatus.Claimed, result);
    }

    [Fact]
    public async Task TryClaimAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = Window();
        var ttl = TimeSpan.FromHours(2);
        var timeToLive = new TimeToLive(ttl);
        var mock = Mocks.Create<IClaimService>();
        await using var provider = TestComposition.BuildElsewhere(Primitives.Claim, mock.Object);
        var service = provider.GetRequiredService<IClaimService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryClaimAsync(
                TestComposition.Key,
                window,
                timeToLive,
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion
}
