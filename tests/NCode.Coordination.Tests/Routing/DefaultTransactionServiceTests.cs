// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultTransactionServiceTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private static RecordTransaction Transaction()
    {
        ReadOnlyMemory<byte> value = (byte[])[1];
        var write = RecordWrite.Put(TestComposition.Key, value, RecordLifetime.Persistent);
        return new RecordTransaction([], [write]);
    }

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var transaction = Transaction();
        var revision = new Revision(4);
        var expected = new TransactionResult(true, revision);
        var mock = Mocks.Create<ITransactionService>();
        mock.Setup(x => x.ExecuteAsync(transaction, null, cancellationToken))
            .ReturnsAsync(expected)
            .Verifiable();
        await using var provider = TestComposition.Build(Primitives.Transaction, mock.Object);
        var service = provider.GetRequiredService<ITransactionService>();

        var result = await service.ExecuteAsync(transaction, null, cancellationToken);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoProviderIsBound_ThrowsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var transaction = Transaction();
        var mock = Mocks.Create<ITransactionService>();
        await using var provider = BuildElsewhere(mock.Object);
        var service = provider.GetRequiredService<ITransactionService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ExecuteAsync(transaction, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion

    private static ServiceProvider BuildElsewhere(ITransactionService implementation)
    {
        var support = PrimitiveSupport.Supported(Primitives.Transaction);
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
