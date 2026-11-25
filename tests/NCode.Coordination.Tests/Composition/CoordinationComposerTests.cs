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

    private TestProvider CreateProvider(ProviderDeclaration declaration) =>
        new(declaration, Mocks.Create<IOccurrenceService>().Object);

    private static ProviderDeclaration Declare(params Guarantee[] guarantees) =>
        new("memory", PrimitiveSupport.Supported(Primitives.Occurrence, guarantees));

    #region Compose Tests

    [Fact]
    public void Compose_WhenValid_ReturnsTheBindings()
    {
        var provider = CreateProvider(Declare(Guarantees.Durable));
        var registrations = Register(provider, new ProviderBinding("memory"));
        registrations.Requirements.Add(
            new FeatureRequirement("replay", Primitives.Occurrence, Guarantees.Durable)
        );

        var composed = CoordinationComposer.Compose(registrations, Services);

        Assert.Equal([provider], composed.Providers);
        var bound = composed.Bindings.Find(Primitives.Occurrence, null);
        Assert.NotNull(bound);
        Assert.Equal("memory", bound.Provider);
    }

    [Fact]
    public void Compose_WhenSeveralStagesFail_ListsEveryFailureInOrder()
    {
        var first = CreateProvider(Declare(Guarantees.Durable));
        var second = CreateProvider(Declare(Guarantees.Durable));
        var registrations = Register(first, new ProviderBinding("memory"));
        registrations.ProviderFactories.Add(_ => second);
        registrations.Requirements.Add(
            new FeatureRequirement("replay", Primitives.Occurrence, Guarantees.ServerClock)
        );

        var exception = Assert.Throws<CoordinationCompositionException>(() =>
            CoordinationComposer.Compose(registrations, Services)
        );

        Assert.Equal(
            [
                "Two providers are named 'memory'.",
                "The feature 'replay' requires 'expiry.server-clock' on the primitive 'occurrence' by default, which the provider 'memory' does not keep.",
            ],
            exception.Failures
        );
    }

    [Fact]
    public void Compose_WhenTheApplicationDefinesAGuarantee_ItCanBeDeclaredAndRequired()
    {
        var custom = new Guarantee("custom.strong");
        var provider = CreateProvider(Declare(custom));
        var registrations = Register(provider, new ProviderBinding("memory"));
        registrations.Definitions.Add(new GuaranteeDefinition(custom, "custom", "Strong."));
        registrations.Requirements.Add(
            new FeatureRequirement("replay", Primitives.Occurrence, custom)
        );

        var composed = CoordinationComposer.Compose(registrations, Services);

        Assert.NotNull(composed.Bindings.Find(Primitives.Occurrence, null));
    }

    [Fact]
    public void Compose_WhenAProviderDefinesAGuarantee_ItsImplicationsCount()
    {
        var custom = new Guarantee("custom.strong");
        var definition = new GuaranteeDefinition(custom, "custom", "Strong.")
        {
            Implies = [Guarantees.Durable],
        };
        var declaration = new ProviderDeclaration(
            "memory",
            PrimitiveSupport.Supported(Primitives.Occurrence, custom)
        )
        {
            Definitions = [definition],
        };
        var provider = CreateProvider(declaration);
        var registrations = Register(provider, new ProviderBinding("memory"));
        registrations.Requirements.Add(
            new FeatureRequirement("replay", Primitives.Occurrence, Guarantees.Durable)
        );

        var composed = CoordinationComposer.Compose(registrations, Services);

        Assert.NotNull(composed.Bindings.Find(Primitives.Occurrence, null));
    }

    [Fact]
    public void Compose_WhenADeclaredGuaranteeIsUndefined_Fails()
    {
        var provider = CreateProvider(Declare(new Guarantee("custom.unknown")));
        var registrations = Register(provider, new ProviderBinding("memory"));

        var exception = Assert.Throws<CoordinationCompositionException>(() =>
            CoordinationComposer.Compose(registrations, Services)
        );

        Assert.Contains(
            "The provider 'memory' declares the guarantee 'custom.unknown' on the primitive 'occurrence', which is not defined.",
            exception.Failures
        );
    }

    #endregion
}
