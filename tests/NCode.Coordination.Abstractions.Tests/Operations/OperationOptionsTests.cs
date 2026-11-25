// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace NCode.Coordination.Tests;

public sealed class OperationOptionsTests
{
    #region Extensions Property Tests

    [Fact]
    public void Extensions_ByDefault_IsEmpty()
    {
        var options = new OperationOptions();

        Assert.Empty(options.Extensions);
    }

    [Fact]
    public void Extensions_WhenValid_AreKept()
    {
        var options = new OperationOptions
        {
            Extensions = ImmutableDictionary<string, string>.Empty.Add("consul.lock-delay", "15s"),
        };

        Assert.Equal("15s", options.Extensions["consul.lock-delay"]);
    }

    [Fact]
    public void Extensions_WhenThereAreTheMaximum_Accepts()
    {
        var values = Enumerable
            .Range(0, CoordinationLimits.MaxExtensions)
            .ToImmutableDictionary(i => $"x.n{i}", _ => "v");

        var options = new OperationOptions { Extensions = values };

        Assert.Equal(CoordinationLimits.MaxExtensions, options.Extensions.Count);
    }

    [Fact]
    public void Extensions_WhenAValueHasTheMaximumLength_Accepts()
    {
        var value = new string('a', CoordinationLimits.MaxExtensionValueLength);
        var values = ImmutableDictionary<string, string>.Empty.Add("x.y", value);

        var options = new OperationOptions { Extensions = values };

        Assert.Equal(value, options.Extensions["x.y"]);
    }

    [Fact]
    public void Extensions_WhenNull_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new OperationOptions { Extensions = null! }
        );

        Assert.Equal("Extensions", exception.ParamName);
    }

    [Fact]
    public void Extensions_WhenThereAreTooMany_Throws()
    {
        var values = Enumerable
            .Range(0, CoordinationLimits.MaxExtensions + 1)
            .ToImmutableDictionary(i => $"x.n{i}", _ => "v");

        var exception = Assert.Throws<ArgumentException>(() =>
            new OperationOptions { Extensions = values }
        );

        Assert.Equal("Extensions", exception.ParamName);
    }

    [Fact]
    public void Extensions_WhenANameIsInvalid_Throws()
    {
        var values = ImmutableDictionary<string, string>.Empty.Add("Bad Name", "v");

        var exception = Assert.Throws<ArgumentException>(() =>
            new OperationOptions { Extensions = values }
        );

        Assert.Equal("Extensions", exception.ParamName);
    }

    [Fact]
    public void Extensions_WhenAValueIsTooLong_Throws()
    {
        var values = ImmutableDictionary<string, string>.Empty.Add(
            "x.y",
            new string('a', CoordinationLimits.MaxExtensionValueLength + 1)
        );

        var exception = Assert.Throws<ArgumentException>(() =>
            new OperationOptions { Extensions = values }
        );

        Assert.Equal("Extensions", exception.ParamName);
    }

    #endregion
}
