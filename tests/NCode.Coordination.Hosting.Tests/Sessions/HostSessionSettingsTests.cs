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

        var result = HostSessionSettings.Create(options);

        var settings = result.Settings;
        Assert.Empty(result.Failures);
        Assert.NotNull(settings);
        var expectedKeyspace = new Keyspace("hosting", "cell-1");
        var expectedGroup = settings.Keyspace.CreateKey("members");
        var expectedTimeToLive = TimeSpan.FromSeconds(30);
        var expectedKeepAliveInterval = TimeSpan.FromSeconds(10);
        var expectedDriftAllowance = TimeSpan.FromSeconds(2);
        var expectedRetryDelay = TimeSpan.FromSeconds(2);
        var expectedMaxRetryDelay = TimeSpan.FromSeconds(30);
        Assert.Equal(expectedKeyspace, settings.Keyspace);
        Assert.Equal(expectedGroup, settings.Group);
        Assert.Equal("node-1", settings.NodeId);
        Assert.Equal("10.0.0.1:5000", settings.Address);
        Assert.Equal(expectedTimeToLive, settings.TimeToLive.Duration);
        Assert.Equal(expectedKeepAliveInterval, settings.KeepAliveInterval);
        Assert.Equal(expectedDriftAllowance, settings.DriftAllowance);
        Assert.Equal(expectedRetryDelay, settings.RetryDelay);
        Assert.Equal(expectedMaxRetryDelay, settings.MaxRetryDelay);
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
    [InlineData(nameof(CoordinationHostOptions.MaxRetryDelay))]
    public void Create_WhenAValueIsInvalid_NamesItsProperty(string property)
    {
        var options = HostingTesting.CreateOptions();
        Break(options, property);

        var result = HostSessionSettings.Create(options);

        Assert.Null(result.Settings);
        var failure = Assert.Single(result.Failures);
        Assert.StartsWith($"{property}: ", failure);
    }

    [Theory]
    [InlineData("KeepAliveIntervalAtMin")]
    [InlineData("KeepAliveIntervalBelowLifetime")]
    [InlineData("KeepAliveIntervalAtMax")]
    [InlineData("RetryDelayAtMin")]
    [InlineData("RetryDelayAtMax")]
    [InlineData("MaxRetryDelayAtRetryDelay")]
    [InlineData("MaxRetryDelayAtMax")]
    public void Create_WhenAPeriodIsAtItsBound_Succeeds(string boundCase)
    {
        var options = HostingTesting.CreateOptions();
        SetPeriods(options, boundCase);

        var result = HostSessionSettings.Create(options);

        Assert.Empty(result.Failures);
        Assert.NotNull(result.Settings);
    }

    [Theory]
    [InlineData("KeepAliveIntervalBelowMin", nameof(CoordinationHostOptions.KeepAliveInterval))]
    [InlineData("KeepAliveIntervalAtLifetime", nameof(CoordinationHostOptions.KeepAliveInterval))]
    [InlineData("KeepAliveIntervalAboveMax", nameof(CoordinationHostOptions.KeepAliveInterval))]
    [InlineData("RetryDelayBelowMin", nameof(CoordinationHostOptions.RetryDelay))]
    [InlineData("RetryDelayAboveMax", nameof(CoordinationHostOptions.RetryDelay))]
    [InlineData("MaxRetryDelayBelowRetryDelay", nameof(CoordinationHostOptions.MaxRetryDelay))]
    [InlineData("MaxRetryDelayAboveMax", nameof(CoordinationHostOptions.MaxRetryDelay))]
    public void Create_WhenAPeriodIsJustPastItsBound_NamesItsProperty(
        string boundCase,
        string property
    )
    {
        var options = HostingTesting.CreateOptions();
        SetPeriods(options, boundCase);

        var result = HostSessionSettings.Create(options);

        Assert.Null(result.Settings);
        Assert.Contains(result.Failures, x =>
            x.StartsWith($"{property}: ", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Create_WhenSeveralValuesAreInvalid_ReportsEach()
    {
        var options = HostingTesting.CreateOptions();
        options.NodeId = "";
        options.RetryDelay = TimeSpan.Zero;

        var result = HostSessionSettings.Create(options);

        var failures = result.Failures;
        Assert.Null(result.Settings);
        Assert.Equal(2, failures.Count);
        Assert.StartsWith($"{nameof(CoordinationHostOptions.NodeId)}: ", failures[0]);
        Assert.StartsWith($"{nameof(CoordinationHostOptions.RetryDelay)}: ", failures[1]);
    }

    #endregion

    #region CreateMember Tests

    [Fact]
    public void CreateMember_WhenGivenASession_CarriesTheNodesIdentity()
    {
        var options = HostingTesting.CreateOptions();
        var result = HostSessionSettings.Create(options);
        var settings = result.Settings;
        Assert.NotNull(settings);
        var session = new SessionId(HostingTesting.Keyspace, "session-1");

        var member = settings.CreateMember(session);

        Assert.Equal(session, member.Session);
        Assert.Equal("node-1", member.NodeId);
        Assert.Equal("10.0.0.1:5000", member.Address);
        Assert.Equal("eu-1", member.Metadata["zone"]);
    }

    #endregion

    #region GetRetryDelay Tests

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(6, 30)]
    [InlineData(int.MaxValue, 30)]
    public void GetRetryDelay_WhenFailuresRepeat_DoublesUpToTheLongest(
        int failures,
        int expectedSeconds
    )
    {
        var options = HostingTesting.CreateOptions();
        var settings = HostSessionSettings.Create(options).Settings;
        Assert.NotNull(settings);

        var delay = settings.GetRetryDelay(failures, 0);

        var expectedDelay = TimeSpan.FromSeconds(expectedSeconds);
        Assert.Equal(expectedDelay, delay);
    }

    [Theory]
    [InlineData(1, 0.5, 1500)]
    [InlineData(3, 0.25, 7000)]
    [InlineData(5, 0.5, 22500)]
    public void GetRetryDelay_WhenJittered_ShortensTheWaitByUpToHalf(
        int failures,
        double jitter,
        int expectedMilliseconds
    )
    {
        var options = HostingTesting.CreateOptions();
        var settings = HostSessionSettings.Create(options).Settings;
        Assert.NotNull(settings);

        var delay = settings.GetRetryDelay(failures, jitter);

        var expectedDelay = TimeSpan.FromMilliseconds(expectedMilliseconds);
        Assert.Equal(expectedDelay, delay);
    }

    [Fact]
    public void GetRetryDelay_WhenFailuresAreBelowOne_Throws()
    {
        var options = HostingTesting.CreateOptions();
        var settings = HostSessionSettings.Create(options).Settings;
        Assert.NotNull(settings);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.GetRetryDelay(0, 0)
        );

        Assert.Equal("failures", exception.ParamName);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    [InlineData(double.NaN)]
    public void GetRetryDelay_WhenTheJitterIsOutOfRange_Throws(double jitter)
    {
        var options = HostingTesting.CreateOptions();
        var settings = HostSessionSettings.Create(options).Settings;
        Assert.NotNull(settings);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.GetRetryDelay(1, jitter)
        );

        Assert.Equal("jitter", exception.ParamName);
    }

    #endregion

    private static void SetPeriods(CoordinationHostOptions options, string boundCase)
    {
        var oneMillisecond = TimeSpan.FromMilliseconds(1);
        var oneTick = TimeSpan.FromTicks(1);
        var oneDay = TimeSpan.FromDays(1);
        var twoDays = TimeSpan.FromDays(2);
        var twoSeconds = TimeSpan.FromSeconds(2);
        var lifetime = TimeSpan.FromSeconds(28);

        switch (boundCase)
        {
            case "KeepAliveIntervalAtMin":
                options.KeepAliveInterval = oneMillisecond;
                break;
            case "KeepAliveIntervalBelowLifetime":
                options.KeepAliveInterval = lifetime - oneTick;
                break;
            case "KeepAliveIntervalAtMax":
                options.SessionTimeToLive = twoDays;
                options.KeepAliveInterval = oneDay;
                break;
            case "RetryDelayAtMin":
                options.RetryDelay = oneMillisecond;
                break;
            case "RetryDelayAtMax":
                options.RetryDelay = oneDay;
                options.MaxRetryDelay = oneDay;
                break;
            case "MaxRetryDelayAtRetryDelay":
                options.MaxRetryDelay = twoSeconds;
                break;
            case "MaxRetryDelayAtMax":
                options.MaxRetryDelay = oneDay;
                break;
            case "KeepAliveIntervalBelowMin":
                options.KeepAliveInterval = oneMillisecond - oneTick;
                break;
            case "KeepAliveIntervalAtLifetime":
                options.KeepAliveInterval = lifetime;
                break;
            case "KeepAliveIntervalAboveMax":
                options.SessionTimeToLive = twoDays;
                options.KeepAliveInterval = oneDay + oneTick;
                break;
            case "RetryDelayBelowMin":
                options.RetryDelay = oneMillisecond - oneTick;
                break;
            case "RetryDelayAboveMax":
                options.RetryDelay = oneDay + oneTick;
                options.MaxRetryDelay = oneDay + oneTick;
                break;
            case "MaxRetryDelayBelowRetryDelay":
                options.MaxRetryDelay = twoSeconds - oneTick;
                break;
            case "MaxRetryDelayAboveMax":
                options.MaxRetryDelay = oneDay + oneTick;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(boundCase), boundCase, null);
        }
    }

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
            case nameof(CoordinationHostOptions.MaxRetryDelay):
                options.MaxRetryDelay = TimeSpan.FromSeconds(1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property), property, null);
        }
    }
}
