// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Hosting.Tests;

namespace NCode.Coordination.Hosting.Sessions.Tests;

public sealed class CoordinationHostOptionsValidatorTests
{
    private CoordinationHostOptionsValidator Validator { get; } = new();

    #region Validate Tests

    [Fact]
    public void Validate_WhenTheOptionsAreValid_Succeeds()
    {
        var options = HostingTesting.CreateOptions();

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenValuesAreInvalid_FailsWithEach()
    {
        var options = HostingTesting.CreateOptions();
        options.NodeId = "";
        options.Address = "";

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
        Assert.StartsWith("NodeId: ", result.Failures.First());
        Assert.StartsWith("Address: ", result.Failures.Last());
    }

    #endregion
}
