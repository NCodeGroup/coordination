// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultNotificationServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        ReadOnlyMemory<byte> payload = (byte[])[1, 2, 3];
        var mock = Mocks.Create<INotificationService>();
        mock.Setup(x => x.PublishAsync(TestComposition.Key, payload, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Notification, mock.Object);
        var service = provider.GetRequiredService<INotificationService>();

        await service.PublishAsync(TestComposition.Key, payload, null, cancellationToken);
    }

    [Fact]
    public async Task PublishAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<INotificationService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<INotificationService>();
        ReadOnlyMemory<byte> payload = (byte[])[1, 2, 3];

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.PublishAsync(TestComposition.Key, payload, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var notification = Notification.Message(TestComposition.Key, 1, (byte[])[1, 2, 3]);
        var stream = TestComposition.Stream(notification);
        var mock = Mocks.Create<INotificationService>();
        mock.Setup(x => x.SubscribeAsync(TestComposition.Key, null, cancellationToken))
            .Returns(stream)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Notification, mock.Object);
        var service = provider.GetRequiredService<INotificationService>();
        var items = new List<Notification>();

        await foreach (
            var item in service.SubscribeAsync(TestComposition.Key, null, cancellationToken)
        )
        {
            items.Add(item);
        }

        var single = Assert.Single(items);
        Assert.Same(notification, single);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(INotificationService implementation)
    {
        var support = PrimitiveSupport.Supported(Primitives.Notification);
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
