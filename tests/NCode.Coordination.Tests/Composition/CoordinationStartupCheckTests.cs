// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class CoordinationStartupCheckTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    public void Dispose() => Mocks.Verify();

    private CoordinationStartupCheck Create(string bindingName)
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var declaration = new ProviderDeclaration(
            "memory",
            PrimitiveSupport.Supported(Primitives.Occurrence, Guarantees.Durable)
        );
        var provider = new TestProvider(declaration, service);
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => provider);
        registrations.Bindings.Add(new ProviderBinding(bindingName));
        return new CoordinationStartupCheck(new CoordinationComposition(registrations, Services));
    }

    #region StartingAsync Tests

    [Fact]
    public async Task StartingAsync_WhenTheCompositionIsInvalid_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        await Assert.ThrowsAsync<CoordinationCompositionException>(() =>
            check.StartingAsync(cancellationToken)
        );
    }

    [Fact]
    public async Task StartingAsync_WhenTheCompositionIsValid_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("memory");

        await check.StartingAsync(cancellationToken);
    }

    #endregion

    #region Other Lifecycle Tests

    [Fact]
    public async Task OtherLifecycleMethods_Complete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        await check.StartAsync(cancellationToken);
        await check.StartedAsync(cancellationToken);
        await check.StoppingAsync(cancellationToken);
        await check.StopAsync(cancellationToken);
        await check.StoppedAsync(cancellationToken);
    }

    #endregion
}
