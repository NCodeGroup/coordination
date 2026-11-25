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
        var revision = new Revision(5);
        var change = RecordChange.Resync(revision);
        var stream = TestComposition.Stream(change);
        var mock = Mocks.Create<IRecordWatchService>();
        mock.Setup(x => x.WatchKeyAsync(TestComposition.Key, null, null, cancellationToken))
            .Returns(stream)
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

        var single = Assert.Single(items);
        Assert.Same(change, single);
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
        var revision = new Revision(5);
        var change = RecordChange.Resync(revision);
        var stream = TestComposition.Stream(change);
        var mock = Mocks.Create<IRecordWatchService>();
        mock.Setup(x => x.WatchPrefixAsync(TestComposition.Key, null, null, cancellationToken))
            .Returns(stream)
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

        var single = Assert.Single(items);
        Assert.Same(change, single);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(IRecordWatchService implementation)
    {
        var support = PrimitiveSupport.Supported(Primitives.RecordWatch);
        var declaration = new ProviderDeclaration(TestComposition.ProviderName, support);
        var binding = new ProviderBinding(TestComposition.ProviderName)
        {
            Keyspace = new Keyspace("elsewhere"),
        };
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddCoordination(b =>
            b.AddProvider(_ => new TestProvider(declaration, implementation)).Bind(binding)
        );
        return services.BuildServiceProvider();
    }
}
