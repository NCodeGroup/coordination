// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class OperationDefinitionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValues()
    {
        var operation = new Operation("lease.renew");
        var primitive = new Primitive("lease");

        var definition = new OperationDefinition(operation, primitive, "consistency", "expiry");

        Assert.Equal(operation, definition.Operation);
        Assert.Equal(primitive, definition.Primitive);
        Assert.Equal<string>(["consistency", "expiry"], definition.Dimensions);
    }

    [Fact]
    public void Constructor_WhenNoDimensions_AcceptsThem()
    {
        var definition = new OperationDefinition(
            new Operation("lease.renew"),
            new Primitive("lease")
        );

        Assert.Empty(definition.Dimensions);
    }

    [Fact]
    public void Constructor_WhenOperationIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new OperationDefinition(null!, new Primitive("lease"))
        );

        Assert.Equal("operation", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenPrimitiveIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new OperationDefinition(new Operation("lease.renew"), null!)
        );

        Assert.Equal("primitive", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenOperationBelongsToAnotherPrimitive_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new OperationDefinition(new Operation("lease.renew"), new Primitive("record"))
        );

        Assert.Equal("operation", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenPrimitiveNameIsOnlyAPrefix_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new OperationDefinition(new Operation("leasex.renew"), new Primitive("lease"))
        );

        Assert.Equal("operation", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenADimensionIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new OperationDefinition(
                new Operation("lease.renew"),
                new Primitive("lease"),
                "consistency",
                null!
            )
        );

        Assert.Equal("dimensions", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenADimensionIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new OperationDefinition(new Operation("lease.renew"), new Primitive("lease"), "Bad")
        );

        Assert.Equal("dimensions", exception.ParamName);
    }

    #endregion
}
