// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespScriptTests
{
    [Fact]
    public void Load_Probe_ReadsServerInfo()
    {
        var script = RespScript.Load("probe");

        Assert.Equal("probe", script.Name);
        Assert.Contains("redis.call('INFO', 'server')", script.Text);
    }

    [Fact]
    public void Load_Missing_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RespScript.Load("missing"));

        Assert.Contains("NCode.Coordination.Resp.Scripts.missing.lua", exception.Message);
    }
}
