// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespScriptTests
{
    #region Load Tests

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

    #endregion

    #region LoadRecord Tests

    [Fact]
    public void LoadRecord_Create_IsThePreludeALineFeedAndTheScript()
    {
        var script = RespScript.LoadRecord("record-create");

        Assert.Equal("record-create", script.Name);
        Assert.Equal(
            RespScript.Load("record-prelude").Text + "\n" + RespScript.Load("record-create").Text,
            script.Text
        );
    }

    [Fact]
    public void LoadRecord_Missing_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RespScript.LoadRecord("missing")
        );

        Assert.Contains("NCode.Coordination.Resp.Scripts.missing.lua", exception.Message);
    }

    [Fact]
    public void RecordTransaction_Always_StartsWithThePrelude()
    {
        var prelude = RespScript.Load("record-prelude").Text;

        Assert.StartsWith(prelude, RespScript.RecordTransaction.Text, StringComparison.Ordinal);
    }

    #endregion
}
