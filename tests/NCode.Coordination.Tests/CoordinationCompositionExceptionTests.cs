// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class CoordinationCompositionExceptionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenFailuresAreGiven_KeepsThemInOrder()
    {
        var exception = new CoordinationCompositionException(["a", "b"]);

        Assert.Equal(["a", "b"], exception.Failures);
    }

    [Fact]
    public void Constructor_WhenFailuresAreNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new CoordinationCompositionException(null!)
        );

        Assert.Equal("failures", exception.ParamName);
    }

    [Fact]
    public void Constructor_WhenFailuresAreEmpty_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CoordinationCompositionException([])
        );

        Assert.Equal("failures", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_WhenAFailureIsBlank_Throws(string? blank)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CoordinationCompositionException(["a", blank!])
        );

        Assert.Equal("failures", exception.ParamName);
    }

    #endregion

    #region Message Tests

    [Fact]
    public void Message_ListsEveryFailure()
    {
        var exception = new CoordinationCompositionException(["a", "b"]);

        Assert.Equal("The coordination composition is invalid:\n- a\n- b", exception.Message);
    }

    #endregion
}
