// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultSequenceServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region NextAsync Tests

    [Fact]
    public async Task NextAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ISequenceService>();
        mock.Setup(x => x.NextAsync(TestComposition.Key, null, cancellationToken))
            .ReturnsAsync(4L)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Sequence, mock.Object);
        var service = provider.GetRequiredService<ISequenceService>();

        var result = await service.NextAsync(TestComposition.Key, null, cancellationToken);

        Assert.Equal(4L, result);
    }

    [Fact]
    public async Task NextAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<ISequenceService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<ISequenceService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.NextAsync(TestComposition.Key, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new SequenceBlock(10, 5);
        var mock = Mocks.Create<ISequenceService>();
        mock.Setup(x => x.ReserveAsync(TestComposition.Key, 5, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Sequence, mock.Object);
        var service = provider.GetRequiredService<ISequenceService>();

        var result = await service.ReserveAsync(TestComposition.Key, 5, null, cancellationToken);

        Assert.Same(expected, result);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(ISequenceService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Sequence)
        );
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddCoordination(b =>
            b.AddProvider(_ => new TestProvider(declaration, implementation))
                .Bind(
                    new ProviderBinding(TestComposition.ProviderName)
                    {
                        Keyspace = new Keyspace("elsewhere"),
                    }
                )
        );
        return services.BuildServiceProvider();
    }
}
