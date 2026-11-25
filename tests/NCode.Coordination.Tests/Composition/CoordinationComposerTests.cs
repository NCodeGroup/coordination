// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class CoordinationComposerTests : IDisposable
{
    private MockRepository Mocks { get; } = new(MockBehavior.Strict);

    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    public void Dispose() => Mocks.Verify();

    private static CoordinationRegistrations Register(
        TestProvider provider,
        params ProviderBinding[] bindings
    )
    {
        var registrations = new CoordinationRegistrations();
        registrations.ProviderFactories.Add(_ => provider);
        registrations.Bindings.AddRange(bindings);
        return registrations;
    }

    private TestProvider CreateProvider(ProviderDeclaration declaration)
    {
        var mock = Mocks.Create<IOccurrenceService>();
        return new TestProvider(declaration, mock.Object);
    }

    private static ProviderDeclaration Declare(params Guarantee[] guarantees)
    {
        var support = PrimitiveSupport.Supported(Primitives.Occurrence, guarantees);
        return new ProviderDeclaration("memory", support);
    }

    #region Compose Tests

    [Fact]
    public void Compose_WhenValid_ReturnsTheBindings()
    {
        var declaration = Declare(Guarantees.Durable);
        var provider = CreateProvider(declaration);
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.Durable
        );
        registrations.Requirements.Add(requirement);

        var composed = CoordinationComposer.Compose(registrations, Services);

        Assert.Equal([provider], composed.Providers);
        var bound = composed.Bindings.Find(Primitives.Occurrence, null);
        Assert.NotNull(bound);
        Assert.Equal("memory", bound.Provider);
    }

    [Fact]
    public void Compose_WhenSeveralStagesFail_ListsEveryFailureInOrder()
    {
        var declaration = Declare(Guarantees.Durable);
        var first = CreateProvider(declaration);
        var second = CreateProvider(declaration);
        var binding = new ProviderBinding("memory");
        var registrations = Register(first, binding);
        registrations.ProviderFactories.Add(_ => second);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.ServerClock
        );
        registrations.Requirements.Add(requirement);

        var composed = CoordinationComposer.Compose(registrations, Services);

        Assert.Equal<string>(
            [
                "Two providers are named 'memory'.",
                "The feature 'replay' requires 'expiry.server-clock' on the primitive 'occurrence' by default, which the provider 'memory' does not keep.",
            ],
            composed.Failures
        );
    }

    [Fact]
    public void Compose_WhenTheApplicationDefinesAGuarantee_ItCanBeDeclaredAndRequired()
    {
        var custom = new Guarantee("custom.strong");
        var declaration = Declare(custom);
        var provider = CreateProvider(declaration);
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);
        var definition = new GuaranteeDefinition(custom, "custom", "Strong.");
        registrations.Definitions.Add(definition);
        var requirement = new FeatureRequirement("replay", Primitives.Occurrence, custom);
        registrations.Requirements.Add(requirement);

        var composed = CoordinationComposer.Compose(registrations, Services);

        var bound = composed.Bindings.Find(Primitives.Occurrence, null);
        Assert.NotNull(bound);
    }

    [Fact]
    public void Compose_WhenAProviderDefinesAGuarantee_ItsImplicationsCount()
    {
        var custom = new Guarantee("custom.strong");
        var definition = new GuaranteeDefinition(custom, "custom", "Strong.")
        {
            Implies = [Guarantees.Durable],
        };
        var support = PrimitiveSupport.Supported(Primitives.Occurrence, custom);
        var declaration = new ProviderDeclaration("memory", support) { Definitions = [definition] };
        var provider = CreateProvider(declaration);
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);
        var requirement = new FeatureRequirement(
            "replay",
            Primitives.Occurrence,
            Guarantees.Durable
        );
        registrations.Requirements.Add(requirement);

        var composed = CoordinationComposer.Compose(registrations, Services);

        var bound = composed.Bindings.Find(Primitives.Occurrence, null);
        Assert.NotNull(bound);
    }

    [Fact]
    public void Compose_WhenADeclaredGuaranteeIsUndefined_Fails()
    {
        var unknown = new Guarantee("custom.unknown");
        var declaration = Declare(unknown);
        var provider = CreateProvider(declaration);
        var binding = new ProviderBinding("memory");
        var registrations = Register(provider, binding);

        var composed = CoordinationComposer.Compose(registrations, Services);

        Assert.Contains(
            "The provider 'memory' declares the guarantee 'custom.unknown' on the primitive 'occurrence', which is not defined.",
            composed.Failures
        );
    }

    #endregion
}
