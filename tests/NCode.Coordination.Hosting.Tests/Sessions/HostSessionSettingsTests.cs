// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Hosting.Tests;

namespace NCode.Coordination.Hosting.Sessions.Tests;

public sealed class HostSessionSettingsTests
{
    #region Create Tests

    [Fact]
    public void Create_WhenTheOptionsAreValid_BuildsTheSettings()
    {
        var options = HostingTesting.CreateOptions();
        options.KeyspaceSegments.Add("cell-1");
        var failures = new List<string>();

        var settings = HostSessionSettings.Create(options, failures);

        Assert.Empty(failures);
        Assert.NotNull(settings);
        Assert.Equal(new Keyspace("hosting", "cell-1"), settings.Keyspace);
        Assert.Equal(settings.Keyspace.CreateKey("members"), settings.Group);
        Assert.Equal("node-1", settings.NodeId);
        Assert.Equal("10.0.0.1:5000", settings.Address);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.TimeToLive.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.DriftAllowance);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.RetryDelay);
        Assert.Equal("eu-1", settings.Metadata["zone"]);
    }

    [Theory]
    [InlineData(nameof(CoordinationHostOptions.Cluster))]
    [InlineData(nameof(CoordinationHostOptions.KeyspaceSegments))]
    [InlineData(nameof(CoordinationHostOptions.Group))]
    [InlineData(nameof(CoordinationHostOptions.NodeId))]
    [InlineData(nameof(CoordinationHostOptions.Address))]
    [InlineData(nameof(CoordinationHostOptions.Metadata))]
    [InlineData(nameof(CoordinationHostOptions.SessionTimeToLive))]
    [InlineData(nameof(CoordinationHostOptions.DriftAllowance))]
    [InlineData(nameof(CoordinationHostOptions.KeepAliveInterval))]
    [InlineData(nameof(CoordinationHostOptions.RetryDelay))]
    public void Create_WhenAValueIsInvalid_NamesItsProperty(string property)
    {
        var options = HostingTesting.CreateOptions();
        Break(options, property);
        var failures = new List<string>();

        var settings = HostSessionSettings.Create(options, failures);

        Assert.Null(settings);
        var failure = Assert.Single(failures);
        Assert.StartsWith($"{property}: ", failure);
    }

    [Fact]
    public void Create_WhenSeveralValuesAreInvalid_ReportsEach()
    {
        var options = HostingTesting.CreateOptions();
        options.NodeId = "";
        options.RetryDelay = TimeSpan.Zero;
        var failures = new List<string>();

        var settings = HostSessionSettings.Create(options, failures);

        Assert.Null(settings);
        Assert.Equal(2, failures.Count);
        Assert.StartsWith($"{nameof(CoordinationHostOptions.NodeId)}: ", failures[0]);
        Assert.StartsWith($"{nameof(CoordinationHostOptions.RetryDelay)}: ", failures[1]);
    }

    #endregion

    #region CreateMember Tests

    [Fact]
    public void CreateMember_WhenGivenASession_CarriesTheNodesIdentity()
    {
        var failures = new List<string>();
        var settings = HostSessionSettings.Create(HostingTesting.CreateOptions(), failures);
        Assert.NotNull(settings);
        var session = new SessionId(HostingTesting.Keyspace, "session-1");

        var member = settings.CreateMember(session);

        Assert.Equal(session, member.Session);
        Assert.Equal("node-1", member.NodeId);
        Assert.Equal("10.0.0.1:5000", member.Address);
        Assert.Equal("eu-1", member.Metadata["zone"]);
    }

    #endregion

    private static void Break(CoordinationHostOptions options, string property)
    {
        switch (property)
        {
            case nameof(CoordinationHostOptions.Cluster):
                options.Cluster = "Not A Cluster";
                break;
            case nameof(CoordinationHostOptions.KeyspaceSegments):
                options.KeyspaceSegments.Add("");
                break;
            case nameof(CoordinationHostOptions.Group):
                options.Group = "";
                break;
            case nameof(CoordinationHostOptions.NodeId):
                options.NodeId = "";
                break;
            case nameof(CoordinationHostOptions.Address):
                options.Address = "";
                break;
            case nameof(CoordinationHostOptions.Metadata):
                options.Metadata["Not A Name"] = "x";
                break;
            case nameof(CoordinationHostOptions.SessionTimeToLive):
                options.SessionTimeToLive = TimeSpan.Zero;
                break;
            case nameof(CoordinationHostOptions.DriftAllowance):
                options.DriftAllowance = TimeSpan.FromSeconds(30);
                break;
            case nameof(CoordinationHostOptions.KeepAliveInterval):
                options.KeepAliveInterval = TimeSpan.FromSeconds(28);
                break;
            case nameof(CoordinationHostOptions.RetryDelay):
                options.RetryDelay = TimeSpan.Zero;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, null);
        }
    }
}
