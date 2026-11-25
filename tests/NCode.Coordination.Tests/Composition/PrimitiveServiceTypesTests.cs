// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Composition.Tests;

public sealed class PrimitiveServiceTypesTests
{
    #region Find Tests

    [Fact]
    public void Find_ForEveryBuiltInPrimitive_ReturnsAnInterface()
    {
        foreach (var primitive in Primitives.All)
        {
            var type = PrimitiveServiceTypes.Find(primitive);

            Assert.NotNull(type);
            Assert.True(type.IsInterface);
        }
    }

    [Fact]
    public void Find_ForAnUnknownPrimitive_ReturnsNull()
    {
        var type = PrimitiveServiceTypes.Find(new Primitive("custom"));

        Assert.Null(type);
    }

    #endregion
}
