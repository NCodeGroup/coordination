// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultClaimServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private static TimeWindow Window() =>
        new(new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1));

    #region TryClaimAsync Tests

    [Fact]
    public async Task TryClaimAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = Window();
        var timeToLive = new TimeToLive(TimeSpan.FromHours(2));
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
        var mock = Mocks.Create<IClaimService>();
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Claim)
        );
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddCoordination(b =>
            b.AddProvider(_ => new TestProvider(declaration, mock.Object))
                .Bind(
                    new ProviderBinding(TestComposition.ProviderName)
                    {
                        Keyspace = new Keyspace("elsewhere"),
                    }
                )
        );
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IClaimService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.TryClaimAsync(
                TestComposition.Key,
                Window(),
                new TimeToLive(TimeSpan.FromHours(2)),
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion
}
