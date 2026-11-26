// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class RecipeTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenGivenNeeds_KeepsThemInOrder()
    {
        var lease = new RecipeNeed(Primitives.Lease, Guarantees.LinearizableUntilFailover);
        var record = new RecipeNeed(Primitives.Record, Guarantees.LinearizableUntilFailover);

        var recipe = new Recipe("sample", lease, record);
        var text = recipe.ToString();

        Assert.Equal("sample", recipe.Name);
        Assert.Equal("sample", text);
        Assert.Equal<RecipeNeed>([lease, record], recipe.Needs);
    }

    [Fact]
    public void Constructor_WhenThereAreNoNeeds_ThrowsNamingTheNeeds()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Recipe("sample"));

        Assert.Equal("needs", exception.ParamName);
        Assert.Contains("at least one primitive", exception.Message);
    }

    [Fact]
    public void Constructor_WhenANeedIsNull_ThrowsNamingTheNeeds()
    {
        RecipeNeed?[] needs = [new RecipeNeed(Primitives.Lease), null];

        var exception = Assert.Throws<ArgumentException>(() => new Recipe("sample", needs!));

        Assert.Equal("needs", exception.ParamName);
        Assert.Contains("A need is null.", exception.Message);
    }

    [Fact]
    public void Constructor_WhenAPrimitiveIsNamedTwice_ThrowsNamingTheNeeds()
    {
        var first = new RecipeNeed(Primitives.Lease, Guarantees.LinearizableUntilFailover);
        var second = new RecipeNeed(Primitives.Lease);

        var exception = Assert.Throws<ArgumentException>(() => new Recipe("sample", first, second));

        Assert.Equal("needs", exception.ParamName);
        Assert.Contains("names each primitive once", exception.Message);
    }

    [Fact]
    public void Constructor_WhenTheNameIsBlank_Throws()
    {
        var need = new RecipeNeed(Primitives.Lease);

        var exception = Assert.Throws<ArgumentException>(() => new Recipe(" ", need));

        Assert.Equal("name", exception.ParamName);
    }

    #endregion
}
