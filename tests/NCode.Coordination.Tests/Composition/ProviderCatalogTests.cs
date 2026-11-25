// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.Tests;

namespace NCode.Coordination.Composition.Tests;

public sealed class ProviderCatalogTests
{
    private static IServiceProvider Services { get; } =
        new ServiceCollection().BuildServiceProvider();

    private static TestProvider CreateProvider(
        string name,
        params IEnumerable<PrimitiveSupport> primitives
    )
    {
        var declaration = new ProviderDeclaration(name, primitives);
        return new TestProvider(declaration);
    }

    #region Create Tests

    [Fact]
    public void Create_WhenProvidersAreValid_KeepsThemInOrder()
    {
        var first = CreateProvider("first");
        var second = CreateProvider("second");

        var checkedCatalog = ProviderCatalog.Create([_ => first, _ => second], Services);
        var catalog = checkedCatalog.Value;

        Assert.Empty(checkedCatalog.Failures);
        Assert.Equal<ICoordinationProvider>([first, second], catalog.Providers);
    }

    [Fact]
    public void Create_WhenAFactoryReturnsNull_Fails()
    {
        var first = CreateProvider("first");

        var checkedCatalog = ProviderCatalog.Create([_ => null!, _ => first], Services);
        var catalog = checkedCatalog.Value;

        Assert.Equal<string>(["A provider factory returned no provider."], checkedCatalog.Failures);
        Assert.Equal<ICoordinationProvider>([first], catalog.Providers);
    }

    [Fact]
    public void Create_WhenTwoProvidersShareAName_FailsAndKeepsTheFirst()
    {
        var first = CreateProvider("memory");
        var second = CreateProvider("memory");

        var checkedCatalog = ProviderCatalog.Create([_ => first, _ => second], Services);
        var catalog = checkedCatalog.Value;

        Assert.Equal<string>(["Two providers are named 'memory'."], checkedCatalog.Failures);
        Assert.Equal<ICoordinationProvider>([first], catalog.Providers);
        var found = catalog.Find("memory");
        Assert.Same(first, found);
    }

    [Fact]
    public void Create_WhenTwoProvidersShareAName_KeepsTheRejectedOneInCreated()
    {
        var first = CreateProvider("memory");
        var second = CreateProvider("memory");

        var checkedCatalog = ProviderCatalog.Create([_ => first, _ => second], Services);
        var catalog = checkedCatalog.Value;

        Assert.Equal<ICoordinationProvider>([first, second], catalog.Created);
    }

    [Fact]
    public void Create_WhenADeclarationListsAPrimitiveTwice_Fails()
    {
        var supported = PrimitiveSupport.Supported(Primitives.Lease);
        var unsupported = PrimitiveSupport.Unsupported(Primitives.Lease, "Not twice.");
        var provider = CreateProvider("memory", supported, unsupported);

        var checkedCatalog = ProviderCatalog.Create([_ => provider], Services);
        var catalog = checkedCatalog.Value;

        Assert.Equal<string>(
            ["The provider 'memory' lists the primitive 'lease' more than once."],
            checkedCatalog.Failures
        );
        Assert.Equal<ICoordinationProvider>([provider], catalog.Providers);
    }

    [Fact]
    public void Create_WhenCalled_PassesTheServicesToEachFactory()
    {
        var received = new List<IServiceProvider>();
        var provider = CreateProvider("memory");

        ProviderCatalog.Create(
            [
                services =>
                {
                    received.Add(services);
                    return provider;
                },
            ],
            Services
        );

        var single = Assert.Single(received);
        Assert.Same(Services, single);
    }

    [Fact]
    public void Create_WhenAFactoryThrows_KeepsTheEarlierProvidersAndTheException()
    {
        var first = CreateProvider("first");
        var third = CreateProvider("third");
        var failure = new InvalidOperationException("The factory failed.");

        var checkedCatalog = ProviderCatalog.Create(
            [_ => first, _ => throw failure, _ => third],
            Services
        );

        var catalog = checkedCatalog.Value;
        Assert.Equal<ICoordinationProvider>([first], catalog.Providers);
        Assert.NotNull(catalog.FactoryFailure);
        var thrown = Assert.Throws<InvalidOperationException>(catalog.FactoryFailure.Throw);
        Assert.Same(failure, thrown);
    }

    #endregion

    #region Find Tests

    [Fact]
    public void Find_WhenNameIsRegistered_ReturnsTheProvider()
    {
        var provider = CreateProvider("memory");
        var checkedCatalog = ProviderCatalog.Create([_ => provider], Services);
        var catalog = checkedCatalog.Value;

        var found = catalog.Find("memory");

        Assert.Same(provider, found);
    }

    [Fact]
    public void Find_WhenNameIsUnknown_ReturnsNull()
    {
        var provider = CreateProvider("memory");
        var checkedCatalog = ProviderCatalog.Create([_ => provider], Services);
        var catalog = checkedCatalog.Value;

        var found = catalog.Find("other");

        Assert.Null(found);
    }

    #endregion
}
