// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultMembershipServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region JoinAsync Tests

    [Fact]
    public async Task JoinAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = new Member(
            new SessionId(TestComposition.Keyspace, "1"),
            "node-1",
            "10.0.0.4:5000"
        );
        var mock = Mocks.Create<IMembershipService>();
        mock.Setup(x => x.JoinAsync(TestComposition.Key, member, null, cancellationToken))
            .ReturnsAsync(MembershipJoinStatus.Joined)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Membership, mock.Object);
        var service = provider.GetRequiredService<IMembershipService>();

        var result = await service.JoinAsync(TestComposition.Key, member, null, cancellationToken);

        Assert.Equal(MembershipJoinStatus.Joined, result);
    }

    [Fact]
    public async Task JoinAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<IMembershipService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<IMembershipService>();
        var member = new Member(
            new SessionId(TestComposition.Keyspace, "1"),
            "node-1",
            "10.0.0.4:5000"
        );

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.JoinAsync(TestComposition.Key, member, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region LeaveAsync Tests

    [Fact]
    public async Task LeaveAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = new SessionId(TestComposition.Keyspace, "1");
        var mock = Mocks.Create<IMembershipService>();
        mock.Setup(x => x.LeaveAsync(TestComposition.Key, id, null, cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Membership, mock.Object);
        var service = provider.GetRequiredService<IMembershipService>();

        await service.LeaveAsync(TestComposition.Key, id, null, cancellationToken);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new MembershipView(TestComposition.Key, new Revision(5));
        var mock = Mocks.Create<IMembershipService>();
        mock.Setup(x => x.ReadAsync(TestComposition.Key, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Membership, mock.Object);
        var service = provider.GetRequiredService<IMembershipService>();

        var result = await service.ReadAsync(TestComposition.Key, null, cancellationToken);

        Assert.Same(expected, result);
    }

    #endregion

    #region WatchAsync Tests

    [Fact]
    public async Task WatchAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var view = new MembershipView(TestComposition.Key, new Revision(5));
        var mock = Mocks.Create<IMembershipService>();
        mock.Setup(x => x.WatchAsync(TestComposition.Key, null, cancellationToken))
            .Returns(TestComposition.Stream(view))
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Membership, mock.Object);
        var service = provider.GetRequiredService<IMembershipService>();
        var items = new List<MembershipView>();

        await foreach (var item in service.WatchAsync(TestComposition.Key, null, cancellationToken))
        {
            items.Add(item);
        }

        Assert.Same(view, Assert.Single(items));
    }

    #endregion

    private static ServiceProvider BuildElsewhere(IMembershipService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Membership)
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
