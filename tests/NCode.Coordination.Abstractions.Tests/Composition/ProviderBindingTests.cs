// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class ProviderBindingTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenArgumentsAreValid_KeepsValues()
    {
        var lease = new Primitive("lease");
        var record = new Primitive("record");

        var binding = new ProviderBinding("memory", lease, record);

        Assert.Equal("memory", binding.Provider);
        Assert.Equal<Primitive>([lease, record], binding.Primitives);
    }

    [Fact]
    public void Constructor_WhenNoPrimitivesAreGiven_HasNoPrimitives()
    {
        var binding = new ProviderBinding("memory");

        Assert.False(binding.Primitives.IsDefault);
        Assert.Empty(binding.Primitives);
    }

    [Fact]
    public void Constructor_WhenProviderIsInvalid_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ProviderBinding("Bad Provider"));

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenProviderIsNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ProviderBinding(null!));

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAPrimitiveIsNull_Throws()
    {
        ImmutableArray<Primitive> primitives = [new Primitive("lease"), null!];

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderBinding("memory", primitives)
        );

        Assert.Equal("primitives", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenAPrimitiveIsListedTwice_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderBinding("memory", new Primitive("lease"), new Primitive("lease"))
        );

        Assert.Equal("primitives", exception.ParamName);
        Assert.StartsWith(
            "A binding lists each primitive once.",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    #endregion

    #region Keyspace Property Tests

    [Fact]
    public void Keyspace_WhenNotSet_IsNull()
    {
        var binding = new ProviderBinding("memory");

        Assert.Null(binding.Keyspace);
    }

    [Fact]
    public void Keyspace_WhenSet_KeepsValue()
    {
        var keyspace = new Keyspace("prod", "cell-1");

        var binding = new ProviderBinding("memory") { Keyspace = keyspace };

        Assert.Equal(keyspace, binding.Keyspace);
    }

    #endregion
}
