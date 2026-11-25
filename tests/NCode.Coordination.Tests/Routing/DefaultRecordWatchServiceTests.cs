// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultRecordWatchServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region WatchKeyAsync Tests

    [Fact]
    public async Task WatchKeyAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var change = RecordChange.Resync(new Revision(5));
        var mock = Mocks.Create<IRecordWatchService>();
        mock.Setup(x => x.WatchKeyAsync(TestComposition.Key, null, null, cancellationToken))
            .Returns(TestComposition.Stream(change))
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.RecordWatch, mock.Object);
        var service = provider.GetRequiredService<IRecordWatchService>();
        var items = new List<RecordChange>();

        await foreach (
            var item in service.WatchKeyAsync(TestComposition.Key, null, null, cancellationToken)
        )
        {
            items.Add(item);
        }

        Assert.Same(change, Assert.Single(items));
    }

    [Fact]
    public void WatchKeyAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<IRecordWatchService>();
        using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<IRecordWatchService>();

        var exception = Assert.Throws<CoordinationException>(() =>
            service.WatchKeyAsync(TestComposition.Key, null, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region WatchPrefixAsync Tests

    [Fact]
    public async Task WatchPrefixAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var change = RecordChange.Resync(new Revision(5));
        var mock = Mocks.Create<IRecordWatchService>();
        mock.Setup(x => x.WatchPrefixAsync(TestComposition.Key, null, null, cancellationToken))
            .Returns(TestComposition.Stream(change))
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.RecordWatch, mock.Object);
        var service = provider.GetRequiredService<IRecordWatchService>();
        var items = new List<RecordChange>();

        await foreach (
            var item in service.WatchPrefixAsync(TestComposition.Key, null, null, cancellationToken)
        )
        {
            items.Add(item);
        }

        Assert.Same(change, Assert.Single(items));
    }

    #endregion

    private static ServiceProvider BuildElsewhere(IRecordWatchService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.RecordWatch)
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
