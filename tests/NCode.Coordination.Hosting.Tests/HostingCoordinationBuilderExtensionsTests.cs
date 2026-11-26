// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace NCode.Coordination.Tests;

public sealed class HostingCoordinationBuilderExtensionsTests : IDisposable
{
    private static ProviderBinding Binding { get; } =
        new(InProcessCoordinationBuilderExtensions.ProviderName);

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    public void Dispose() => Mocks.Verify();

    #region RequireHostSession Tests

    [Fact]
    public async Task RequireHostSession_WhenNoProviderIsBound_FailsTheStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Services.AddCoordination(b => b.RequireHostSession());
        using IHost host = builder.Build();

        var exception = await Assert.ThrowsAsync<CoordinationCompositionException>(() =>
            host.StartAsync(cancellationToken)
        );

        Assert.Equal<string>(
            [
                "The feature 'host-session' requires the primitive 'session' by default, and no provider is bound to it.",
                "The feature 'host-session' requires the primitive 'membership' by default, and no provider is bound to it.",
            ],
            exception.Failures
        );
    }

    [Fact]
    public async Task RequireHostSession_WhenTheProviderIsNotLinearizable_FailsTheStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Services.AddMetrics();
        builder.Services.AddCoordination(b =>
            b.AddInProcessProvider().Bind(Binding).RequireHostSession()
        );
        using IHost host = builder.Build();

        var exception = await Assert.ThrowsAsync<CoordinationCompositionException>(() =>
            host.StartAsync(cancellationToken)
        );

        var failure = Assert.Single(exception.Failures);
        Assert.Equal(
            "The feature 'host-session' requires 'consistency.linearizable-until-failover' on the primitive 'session' by default, which the provider 'in-process' does not keep.",
            failure
        );
    }

    [Fact]
    public async Task RequireHostSession_WithAKeyspace_ChecksItsBinding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        var keyspace = new Keyspace("hosting");
        builder.Services.AddCoordination(b => b.RequireHostSession(keyspace));
        using IHost host = builder.Build();

        var exception = await Assert.ThrowsAsync<CoordinationCompositionException>(() =>
            host.StartAsync(cancellationToken)
        );

        Assert.Equal(2, exception.Failures.Length);
        Assert.All(exception.Failures, failure =>
            Assert.Contains("for the keyspace 'hosting'", failure)
        );
        Assert.DoesNotContain(exception.Failures, failure => failure.Contains("by default"));
    }

    [Fact]
    public async Task RequireHostSession_WhenASingleNodeBindsTheProvider_Starts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = new HostApplicationBuilderSettings();
        var builder = Host.CreateEmptyApplicationBuilder(settings);
        builder.Services.AddMetrics();
        builder.Services.AddCoordination(b =>
            b.AddInProcessProvider().Bind(Binding).UseSingleNode().RequireHostSession()
        );
        using IHost host = builder.Build();

        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    [Fact]
    public void RequireHostSession_WhenTheKeyspaceIsNull_Throws()
    {
        var builder = Mocks.Create<ICoordinationBuilder>();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            builder.Object.RequireHostSession(null!)
        );

        Assert.Equal("keyspace", exception.ParamName);
    }

    #endregion
}
