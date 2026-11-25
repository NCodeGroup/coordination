// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultRecordServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private static StoredRecord Stored() =>
        new(TestComposition.Key, new byte[] { 1 }, new Revision(3));

    private static RecordLifetime Lifetime() =>
        RecordLifetime.ForTimeToLive(new TimeToLive(TimeSpan.FromSeconds(30)));

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        ReadOnlyMemory<byte> value = new byte[] { 1, 2 };
        var lifetime = Lifetime();
        var expected = RecordWriteResult.Written(new Revision(1));
        var mock = Mocks.Create<IRecordService>();
        mock.Setup(x =>
                x.CreateAsync(TestComposition.Key, value, lifetime, null, cancellationToken)
            )
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Record, mock.Object);
        var service = provider.GetRequiredService<IRecordService>();

        var result = await service.CreateAsync(
            TestComposition.Key,
            value,
            lifetime,
            null,
            cancellationToken
        );

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task CreateAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<IRecordService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<IRecordService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.CreateAsync(
                TestComposition.Key,
                ReadOnlyMemory<byte>.Empty,
                Lifetime(),
                null,
                cancellationToken
            )
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    #region ReadAsync Tests

    [Fact]
    public async Task ReadAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<IRecordService>();
        mock.Setup(x => x.ReadAsync(TestComposition.Key, null, cancellationToken))
            .ReturnsAsync((StoredRecord?)null)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Record, mock.Object);
        var service = provider.GetRequiredService<IRecordService>();

        var result = await service.ReadAsync(TestComposition.Key, null, cancellationToken);

        Assert.Null(result);
    }

    #endregion

    #region CompareAndSwapAsync Tests

    [Fact]
    public async Task CompareAndSwapAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedRevision = new Revision(1);
        ReadOnlyMemory<byte> value = new byte[] { 1, 2 };
        var lifetime = Lifetime();
        var expected = RecordWriteResult.Written(new Revision(2));
        var mock = Mocks.Create<IRecordService>();
        mock.Setup(x =>
                x.CompareAndSwapAsync(
                    TestComposition.Key,
                    expectedRevision,
                    value,
                    lifetime,
                    null,
                    cancellationToken
                )
            )
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Record, mock.Object);
        var service = provider.GetRequiredService<IRecordService>();

        var result = await service.CompareAndSwapAsync(
            TestComposition.Key,
            expectedRevision,
            value,
            lifetime,
            null,
            cancellationToken
        );

        Assert.Same(expected, result);
    }

    #endregion

    #region CompareAndDeleteAsync Tests

    [Fact]
    public async Task CompareAndDeleteAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedRevision = new Revision(1);
        var mock = Mocks.Create<IRecordService>();
        mock.Setup(x =>
                x.CompareAndDeleteAsync(
                    TestComposition.Key,
                    expectedRevision,
                    null,
                    cancellationToken
                )
            )
            .ReturnsAsync(RecordDeleteStatus.Deleted)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Record, mock.Object);
        var service = provider.GetRequiredService<IRecordService>();

        var result = await service.CompareAndDeleteAsync(
            TestComposition.Key,
            expectedRevision,
            null,
            cancellationToken
        );

        Assert.Equal(RecordDeleteStatus.Deleted, result);
    }

    #endregion

    #region TakeAsync Tests

    [Fact]
    public async Task TakeAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mock = Mocks.Create<IRecordService>();
        mock.Setup(x => x.TakeAsync(TestComposition.Key, null, cancellationToken))
            .ReturnsAsync((StoredRecord?)null)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Record, mock.Object);
        var service = provider.GetRequiredService<IRecordService>();

        var result = await service.TakeAsync(TestComposition.Key, null, cancellationToken);

        Assert.Null(result);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = new RecordPageRequest(10);
        var expected = new RecordPage(new Revision(4), Stored());
        var mock = Mocks.Create<IRecordService>();
        mock.Setup(x => x.ListAsync(TestComposition.Key, page, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Record, mock.Object);
        var service = provider.GetRequiredService<IRecordService>();

        var result = await service.ListAsync(TestComposition.Key, page, null, cancellationToken);

        Assert.Same(expected, result);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(IRecordService implementation)
    {
        var declaration = new ProviderDeclaration(
            TestComposition.ProviderName,
            PrimitiveSupport.Supported(Primitives.Record)
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
