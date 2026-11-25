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
        var sessionId = new SessionId(TestComposition.Keyspace, "1");
        var member = new Member(sessionId, "node-1", "10.0.0.4:5000");
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
        var sessionId = new SessionId(TestComposition.Keyspace, "1");
        var member = new Member(sessionId, "node-1", "10.0.0.4:5000");

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
        var revision = new Revision(5);
        var expected = new MembershipView(TestComposition.Key, revision);
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
        var revision = new Revision(5);
        var view = new MembershipView(TestComposition.Key, revision);
        var stream = TestComposition.Stream(view);
        var mock = Mocks.Create<IMembershipService>();
        mock.Setup(x => x.WatchAsync(TestComposition.Key, null, cancellationToken))
            .Returns(stream)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Membership, mock.Object);
        var service = provider.GetRequiredService<IMembershipService>();
        var items = new List<MembershipView>();

        await foreach (var item in service.WatchAsync(TestComposition.Key, null, cancellationToken))
        {
            items.Add(item);
        }

        var single = Assert.Single(items);
        Assert.Same(view, single);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(IMembershipService implementation)
    {
        var support = PrimitiveSupport.Supported(Primitives.Membership);
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
