// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Coordination.Resp.Server;

namespace NCode.Coordination.Resp.Counters.Tests;

public sealed class RespCounterServiceTests
{
    #region LargestCount Tests

    [Fact]
    public void LargestCount_Always_IsTheScriptsLargestCount()
    {
        var largest = RespCounterService.LargestCount.ToString(CultureInfo.InvariantCulture);
        var expected = $"local largest = {largest}";
        var text = RespScript.CounterIncrement.Text;

        Assert.Contains(expected, text);
    }

    #endregion
}
