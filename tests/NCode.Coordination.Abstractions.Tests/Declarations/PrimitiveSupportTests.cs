// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class PrimitiveSupportTests
{
    private const string Reason = "The store has no leases.";

    #region Supported Tests

    [Fact]
    public void Supported_WhenArgumentsAreValid_KeepsPrimitiveAndGuarantees()
    {
        var primitive = new Primitive("lease");
        var durable = new Guarantee("durable");
        var linearizable = new Guarantee("linearizable");

        var support = PrimitiveSupport.Supported(primitive, durable, linearizable);

        Assert.Same(primitive, support.Primitive);
        Assert.Equal<Guarantee>([durable, linearizable], support.Guarantees);
    }

    [Fact]
    public void Supported_WhenCalled_IsSupportedWithNoReason()
    {
        var support = PrimitiveSupport.Supported(new Primitive("lease"));

        Assert.True(support.IsSupported);
        Assert.Null(support.UnsupportedReason);
    }

    [Fact]
    public void Supported_WhenNoGuaranteesAreGiven_HasNoGuarantees()
    {
        var support = PrimitiveSupport.Supported(new Primitive("lease"));

        Assert.False(support.Guarantees.IsDefault);
        Assert.Empty(support.Guarantees);
    }

    [Fact]
    public void Supported_WhenPrimitiveIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            PrimitiveSupport.Supported(null!)
        );

        Assert.Equal("primitive", exception.ParamName);
    }

    [Fact]
    public void Supported_WhenAGuaranteeIsNull_Throws()
    {
        ImmutableArray<Guarantee> guarantees = [new Guarantee("durable"), null!];

        var exception = Assert.Throws<ArgumentException>(() =>
            PrimitiveSupport.Supported(new Primitive("lease"), guarantees)
        );

        Assert.Equal("guarantees", exception.ParamName);
    }

    #endregion

    #region Unsupported Tests

    [Fact]
    public void Unsupported_WhenArgumentsAreValid_KeepsPrimitiveAndReason()
    {
        var primitive = new Primitive("lease");

        var support = PrimitiveSupport.Unsupported(primitive, Reason);

        Assert.Same(primitive, support.Primitive);
        Assert.Equal(Reason, support.UnsupportedReason);
    }

    [Fact]
    public void Unsupported_WhenCalled_IsNotSupportedAndHasNoGuarantees()
    {
        var support = PrimitiveSupport.Unsupported(new Primitive("lease"), Reason);

        Assert.False(support.IsSupported);
        Assert.False(support.Guarantees.IsDefault);
        Assert.Empty(support.Guarantees);
    }

    [Fact]
    public void Unsupported_WhenPrimitiveIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            PrimitiveSupport.Unsupported(null!, Reason)
        );

        Assert.Equal("primitive", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Unsupported_WhenReasonIsBlank_Throws(string reason)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PrimitiveSupport.Unsupported(new Primitive("lease"), reason)
        );

        Assert.Equal("reason", exception.ParamName);
    }

    [Fact]
    public void Unsupported_WhenReasonIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            PrimitiveSupport.Unsupported(new Primitive("lease"), null!)
        );

        Assert.Equal("reason", exception.ParamName);
    }

    #endregion
}
