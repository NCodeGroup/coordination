// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecipeNeedTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenTheSourceListChanges_KeepsTheCopiedGuarantees()
    {
        List<Guarantee> source = [Guarantees.LinearizableUntilFailover];

        var need = new RecipeNeed(Primitives.Lease, source);
        source.Add(Guarantees.Durable);

        Assert.Equal<Guarantee>([Guarantees.LinearizableUntilFailover], need.Guarantees);
        Assert.Same(Primitives.Lease, need.Primitive);
    }

    [Fact]
    public void Constructor_WhenAGuaranteeIsNull_ThrowsNamingTheGuarantees()
    {
        Guarantee?[] guarantees = [Guarantees.Durable, null];

        var exception = Assert.Throws<ArgumentException>(() =>
            new RecipeNeed(Primitives.Lease, guarantees!)
        );

        Assert.Equal("guarantees", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenThereAreNoGuarantees_HasAnEmptyArray()
    {
        var need = new RecipeNeed(Primitives.Membership);

        Assert.False(need.Guarantees.IsDefault);
        Assert.Empty(need.Guarantees);
    }

    #endregion
}
