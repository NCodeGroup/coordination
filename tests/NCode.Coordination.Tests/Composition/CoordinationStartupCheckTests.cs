// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class CoordinationStartupCheckTests : IDisposable
{
    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    private CoordinationStartupCheck Create(
        string bindingName,
        Func<CancellationToken, ValueTask>? probe = null
    )
    {
        var service = Mocks.Create<IOccurrenceService>().Object;
        var support = PrimitiveSupport.Supported(Primitives.Occurrence, Guarantees.Durable);
        var declaration = new ProviderDeclaration("memory", support);
        var provider = new TestProvider(declaration, service)
        {
            Probe = probe ?? (_ => ValueTask.CompletedTask),
        };
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => provider);
        var binding = new ProviderBinding(bindingName);
        registrations.Bindings.Add(binding);
        var composition = new CoordinationComposition(registrations, Services);
        return new CoordinationStartupCheck(composition);
    }

    #region StartingAsync Tests

    [Fact]
    public async Task StartingAsync_WhenTheCompositionIsInvalid_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        var thrown = await Assert.ThrowsAsync<CoordinationCompositionException>(() =>
            check.StartingAsync(cancellationToken)
        );

        Assert.Equal<string>(
            ["A binding names the provider 'missing', which is not registered."],
            thrown.Failures
        );
    }

    [Fact]
    public async Task StartingAsync_WhenTheCompositionIsValid_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("memory");

        await check.StartingAsync(cancellationToken);
    }

    [Fact]
    public async Task StartingAsync_WhenAProbeThrows_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new CoordinationException(
            CoordinationErrorCodes.Unsupported,
            outcomeUnknown: false,
            "probe"
        );
        var check = Create("memory", _ => ValueTask.FromException(failure));

        var thrown = await Assert.ThrowsAsync<CoordinationException>(() =>
            check.StartingAsync(cancellationToken)
        );

        Assert.Same(failure, thrown);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenCalled_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        await check.StartAsync(cancellationToken);
    }

    #endregion

    #region StartedAsync Tests

    [Fact]
    public async Task StartedAsync_WhenCalled_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        await check.StartedAsync(cancellationToken);
    }

    #endregion

    #region StoppingAsync Tests

    [Fact]
    public async Task StoppingAsync_WhenCalled_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        await check.StoppingAsync(cancellationToken);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_WhenCalled_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        await check.StopAsync(cancellationToken);
    }

    #endregion

    #region StoppedAsync Tests

    [Fact]
    public async Task StoppedAsync_WhenCalled_Completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var check = Create("missing");

        await check.StoppedAsync(cancellationToken);
    }

    #endregion
}
