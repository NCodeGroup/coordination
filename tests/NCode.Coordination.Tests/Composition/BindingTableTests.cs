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

    private static BindingTable Build(params BoundPrimitive[] bound)
    {
        var failures = new List<string>();
        var table = BindingTable.Create(bound, failures);
        Assert.Empty(failures);
        return table;
    }

    #region Find Tests

    [Fact]
    public void Find_WhenOnlyADefaultIsBound_ReturnsItForAnyKeyspace()
    {
        var entry = Bound("a", Primitives.Lease, null);
        var table = Build(entry);

        Assert.Same(entry, table.Find(Primitives.Lease, Tenant));
        Assert.Same(entry, table.Find(Primitives.Lease, Other));
    }

    [Fact]
    public void Find_WhenAKeyspaceIsBound_ReturnsItForThatKeyspaceAndThoseUnderIt()
    {
        var entry = Bound("a", Primitives.Lease, Cell);
        var table = Build(entry);

        Assert.Same(entry, table.Find(Primitives.Lease, Cell));
        Assert.Same(entry, table.Find(Primitives.Lease, Tenant));
        Assert.Null(table.Find(Primitives.Lease, Prod));
    }

    [Fact]
    public void Find_WhenScopesNest_ReturnsTheLongest()
    {
        var outer = Bound("a", Primitives.Lease, Prod);
        var inner = Bound("b", Primitives.Lease, Cell);
        var fallback = Bound("c", Primitives.Lease, null);
        var table = Build(inner, fallback, outer);

        Assert.Same(inner, table.Find(Primitives.Lease, Tenant));
        Assert.Same(inner, table.Find(Primitives.Lease, Cell));
        Assert.Same(outer, table.Find(Primitives.Lease, Prod));
        Assert.Same(fallback, table.Find(Primitives.Lease, Other));
    }

    [Fact]
    public void Find_WhenTheClusterDiffers_ReturnsTheDefault()
    {
        var scoped = Bound("a", Primitives.Lease, Prod);
        var fallback = Bound("b", Primitives.Lease, null);
        var table = Build(scoped, fallback);

        Assert.Same(fallback, table.Find(Primitives.Lease, Other));
    }

    [Fact]
    public void Find_WhenNothingCovers_ReturnsNull()
    {
        var table = Build(Bound("a", Primitives.Lease, Cell));

        Assert.Null(table.Find(Primitives.Lease, Other));
    }

    [Fact]
    public void Find_WhenKeyspaceIsNull_ReturnsOnlyTheDefault()
    {
        var scoped = Bound("a", Primitives.Lease, Prod);
        var scopedOnly = Build(scoped);
        Assert.Null(scopedOnly.Find(Primitives.Lease, null));

        var fallback = Bound("b", Primitives.Lease, null);
        var table = Build(scoped, fallback);
        Assert.Same(fallback, table.Find(Primitives.Lease, null));
    }

    [Fact]
    public void Find_WhenThePrimitiveDiffers_ReturnsNull()
    {
        var table = Build(Bound("a", Primitives.Lease, null));

        Assert.Null(table.Find(Primitives.Record, Prod));
    }

    #endregion

    #region Create Tests

    [Fact]
    public void Create_WhenTwoDefaultsBindAPrimitive_Fails()
    {
        var failures = new List<string>();

        BindingTable.Create(
            [Bound("a", Primitives.Lease, null), Bound("b", Primitives.Lease, null)],
            failures
        );

        Assert.Equal(
            ["The primitive 'lease' is bound twice by default: to 'a' and to 'b'."],
            failures
        );
    }

    [Fact]
    public void Create_WhenTwoBindingsShareAKeyspace_Fails()
    {
        var failures = new List<string>();

        BindingTable.Create(
            [
                Bound("a", Primitives.Lease, Cell),
                Bound("b", Primitives.Lease, new Keyspace("prod", "cell-1")),
            ],
            failures
        );

        Assert.Equal(
            [
                "The primitive 'lease' is bound twice for the keyspace 'prod/cell-1': to 'a' and to 'b'.",
            ],
            failures
        );
    }

    [Fact]
    public void Create_WhenOneScopeNestsInAnother_DoesNotFail()
    {
        var failures = new List<string>();

        BindingTable.Create(
            [
                Bound("a", Primitives.Lease, null),
                Bound("b", Primitives.Lease, Prod),
                Bound("c", Primitives.Lease, Cell),
            ],
            failures
        );

        Assert.Empty(failures);
    }

    #endregion
}
