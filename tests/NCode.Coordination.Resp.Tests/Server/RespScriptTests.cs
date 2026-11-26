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

    #region LoadKeyspace Tests

    [Fact]
    public void LoadKeyspace_Create_IsThePreludeALineFeedAndTheScript()
    {
        var prelude = RespScript.Load("keyspace-prelude");
        var create = RespScript.Load("record-create");
        var expected = prelude.Text + "\n" + create.Text;

        var script = RespScript.LoadKeyspace("record-create");

        Assert.Equal("record-create", script.Name);
        Assert.Equal(expected, script.Text);
    }

    [Fact]
    public void LoadKeyspace_Missing_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RespScript.LoadKeyspace("missing")
        );

        Assert.Contains("NCode.Coordination.Resp.Scripts.missing.lua", exception.Message);
    }

    [Fact]
    public void RecordTransaction_Always_StartsWithThePrelude()
    {
        var prelude = RespScript.Load("keyspace-prelude").Text;

        Assert.StartsWith(prelude, RespScript.RecordTransaction.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyspaceRevision_Always_StartsWithThePreludeAndReadsTheRevision()
    {
        var prelude = RespScript.Load("keyspace-prelude").Text;

        Assert.StartsWith(prelude, RespScript.KeyspaceRevision.Text, StringComparison.Ordinal);
        Assert.Equal("keyspace-revision", RespScript.KeyspaceRevision.Name);
    }

    #endregion
}
