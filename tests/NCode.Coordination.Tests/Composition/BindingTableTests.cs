// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Composition.Tests;

public sealed class BindingTableTests
{
    private static Keyspace Prod { get; } = new("prod");

    private static Keyspace Cell { get; } = new("prod", "cell-1");

    private static Keyspace Tenant { get; } = new("prod", "cell-1", "t1");

    private static Keyspace Other { get; } = new("other");

    private static BoundPrimitive Bound(string provider, Primitive primitive, Keyspace? keyspace) =>
        new(provider, primitive, keyspace, null, ImmutableHashSet<Guarantee>.Empty);

    private static BindingTable Build(params IEnumerable<BoundPrimitive> bound)
    {
        var checkedTable = BindingTable.Create(bound);
        Assert.Empty(checkedTable.Failures);
        return checkedTable.Value;
    }

    #region Find Tests

    [Fact]
    public void Find_WhenOnlyADefaultIsBound_ReturnsItForAnyKeyspace()
    {
        var entry = Bound("a", Primitives.Lease, null);
        var table = Build(entry);

        var foundTenant = table.Find(Primitives.Lease, Tenant);
        Assert.Same(entry, foundTenant);
        var foundOther = table.Find(Primitives.Lease, Other);
        Assert.Same(entry, foundOther);
    }

    [Fact]
    public void Find_WhenAKeyspaceIsBound_ReturnsItForThatKeyspaceAndThoseUnderIt()
    {
        var entry = Bound("a", Primitives.Lease, Cell);
        var table = Build(entry);

        var foundCell = table.Find(Primitives.Lease, Cell);
        Assert.Same(entry, foundCell);
        var foundTenant = table.Find(Primitives.Lease, Tenant);
        Assert.Same(entry, foundTenant);
        var foundProd = table.Find(Primitives.Lease, Prod);
        Assert.Null(foundProd);
    }

    [Fact]
    public void Find_WhenScopesNest_ReturnsTheLongest()
    {
        var outer = Bound("a", Primitives.Lease, Prod);
        var inner = Bound("b", Primitives.Lease, Cell);
        var fallback = Bound("c", Primitives.Lease, null);
        var table = Build(inner, fallback, outer);

        var foundTenant = table.Find(Primitives.Lease, Tenant);
        Assert.Same(inner, foundTenant);
        var foundCell = table.Find(Primitives.Lease, Cell);
        Assert.Same(inner, foundCell);
        var foundProd = table.Find(Primitives.Lease, Prod);
        Assert.Same(outer, foundProd);
        var foundOther = table.Find(Primitives.Lease, Other);
        Assert.Same(fallback, foundOther);
    }

    [Fact]
    public void Find_WhenTheClusterDiffers_ReturnsTheDefault()
    {
        var scoped = Bound("a", Primitives.Lease, Prod);
        var fallback = Bound("b", Primitives.Lease, null);
        var table = Build(scoped, fallback);

        var found = table.Find(Primitives.Lease, Other);
        Assert.Same(fallback, found);
    }

    [Fact]
    public void Find_WhenNothingCovers_ReturnsNull()
    {
        var entry = Bound("a", Primitives.Lease, Cell);
        var table = Build(entry);

        var found = table.Find(Primitives.Lease, Other);
        Assert.Null(found);
    }

    [Fact]
    public void Find_WhenKeyspaceIsNull_ReturnsOnlyTheDefault()
    {
        var scoped = Bound("a", Primitives.Lease, Prod);
        var scopedOnly = Build(scoped);
        var foundInScopedOnly = scopedOnly.Find(Primitives.Lease, null);
        Assert.Null(foundInScopedOnly);

        var fallback = Bound("b", Primitives.Lease, null);
        var table = Build(scoped, fallback);
        var foundInTable = table.Find(Primitives.Lease, null);
        Assert.Same(fallback, foundInTable);
    }

    [Fact]
    public void Find_WhenThePrimitiveDiffers_ReturnsNull()
    {
        var entry = Bound("a", Primitives.Lease, null);
        var table = Build(entry);

        var found = table.Find(Primitives.Record, Prod);
        Assert.Null(found);
    }

    #endregion

    #region Create Tests

    [Fact]
    public void Create_WhenTwoDefaultsBindAPrimitive_Fails()
    {
        var first = Bound("a", Primitives.Lease, null);
        var second = Bound("b", Primitives.Lease, null);

        var checkedTable = BindingTable.Create([first, second]);

        Assert.Equal(
            ["The primitive 'lease' is bound twice by default: to 'a' and to 'b'."],
            checkedTable.Failures
        );
    }

    [Fact]
    public void Create_WhenTwoBindingsShareAKeyspace_Fails()
    {
        var sameCell = new Keyspace("prod", "cell-1");
        var first = Bound("a", Primitives.Lease, Cell);
        var second = Bound("b", Primitives.Lease, sameCell);

        var checkedTable = BindingTable.Create([first, second]);

        Assert.Equal(
            [
                "The primitive 'lease' is bound twice for the keyspace 'prod/cell-1': to 'a' and to 'b'.",
            ],
            checkedTable.Failures
        );
    }

    [Fact]
    public void Create_WhenOneScopeNestsInAnother_DoesNotFail()
    {
        var first = Bound("a", Primitives.Lease, null);
        var second = Bound("b", Primitives.Lease, Prod);
        var third = Bound("c", Primitives.Lease, Cell);

        var checkedTable = BindingTable.Create([first, second, third]);

        Assert.Empty(checkedTable.Failures);
    }

    #endregion
}
