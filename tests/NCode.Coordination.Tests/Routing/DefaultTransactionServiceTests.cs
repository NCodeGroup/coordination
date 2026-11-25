// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Routing.Tests;

public sealed class DefaultTransactionServiceTests : IDisposable
{
    private static RecordTransaction Transaction()
    {
        ReadOnlyMemory<byte> value = (byte[])[1];
        var write = RecordWrite.Put(TestComposition.Key, value, RecordLifetime.Persistent);
        return new RecordTransaction([], [write]);
    }

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenBound_ReturnsTheProvidersResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var transaction = Transaction();
        var revision = new Revision(4);
        var expected = new TransactionResult(TransactionStatus.Succeeded, revision);
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
        await using var provider = TestComposition.BuildElsewhere(
            Primitives.Transaction,
            mock.Object
        );
        var service = provider.GetRequiredService<ITransactionService>();

        var exception = await Assert.ThrowsAsync<CoordinationException>(async () =>
            await service.ExecuteAsync(transaction, null, cancellationToken)
        );

        Assert.Equal(CoordinationErrorCodes.Unsupported, exception.Code);
    }

    #endregion
}
