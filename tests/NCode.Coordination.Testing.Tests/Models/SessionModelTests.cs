// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Testing.Tests;

namespace NCode.Coordination.Testing.Models.Tests;

public sealed class SessionModelTests
{
    private static Keyspace Space { get; } = new("test");

    private static SessionId Id { get; } = new(Space, "s0");

    private static TimeToLive Lifetime { get; } = new(TimeSpan.FromSeconds(30));

    private static SessionModel CreateModel() => new();

    private static SessionCall Open { get; } = new(SessionCall.Open);

    private static SessionCall KeepAlive { get; } = new(SessionCall.KeepAlive);

    private static SessionCall Close { get; } = new(SessionCall.Close);

    private static SessionGrant Grant { get; } = new(Id, Lifetime);

    private static SessionState OpenState { get; } = new(Id, false);

    #region TryApply Tests

    [Fact]
    public void TryApply_WhenOpenReturnsAGrant_OpensTheSession()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Open, Grant);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(OpenState, next);
    }

    [Fact]
    public void TryApply_WhenOpenReturnsAGrantTwice_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Open, Grant);

        var applied = model.TryApply(OpenState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenOpenReturnsNoGrant_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Open, 1L);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenKeepAliveIsRenewedWhileOpen_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(KeepAlive, KeepAliveResult.Renewed(Lifetime));

        var applied = model.TryApply(OpenState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(OpenState, next);
    }

    [Fact]
    public void TryApply_WhenKeepAliveIsRenewedAfterClose_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(KeepAlive, KeepAliveResult.Renewed(Lifetime));

        var applied = model.TryApply(OpenState with { Ended = true }, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenKeepAliveEndedWhileOpen_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(KeepAlive, KeepAliveResult.Ended);

        var applied = model.TryApply(OpenState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenKeepAliveEndedAfterClose_KeepsTheState()
    {
        var model = CreateModel();
        var ended = OpenState with { Ended = true };
        var operation = HistoryTesting.Returned(KeepAlive, KeepAliveResult.Ended);

        var applied = model.TryApply(ended, operation, out var next);

        Assert.True(applied);
        Assert.Equal(ended, next);
    }

    [Fact]
    public void TryApply_WhenKeepAliveReturnsANumber_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(KeepAlive, 1L);

        var applied = model.TryApply(OpenState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenKeepAliveComesBeforeOpen_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(KeepAlive, KeepAliveResult.Ended);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenCloseReturns_EndsTheSession()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Close, null);

        var applied = model.TryApply(OpenState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(OpenState with { Ended = true }, next);
    }

    [Fact]
    public void TryApply_WhenCloseComesBeforeOpen_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(Close, null);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenOpenIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Open);

        var applied = model.TryApply(model.Initial, operation, out var next);

        Assert.True(applied);
        Assert.Equal(model.Initial, next);
    }

    [Fact]
    public void TryApply_WhenKeepAliveIsIndeterminate_KeepsTheState()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(KeepAlive);

        var applied = model.TryApply(OpenState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(OpenState, next);
    }

    [Fact]
    public void TryApply_WhenCloseIsIndeterminate_EndsTheSession()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Indeterminate(Close);

        var applied = model.TryApply(OpenState, operation, out var next);

        Assert.True(applied);
        Assert.Equal(OpenState with { Ended = true }, next);
    }

    [Fact]
    public void TryApply_WhenOperationIsUnknown_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned(new SessionCall("other"), null);

        var applied = model.TryApply(OpenState, operation, out _);

        Assert.False(applied);
    }

    [Fact]
    public void TryApply_WhenInputIsForeign_Rejects()
    {
        var model = CreateModel();
        var operation = HistoryTesting.Returned("open", Grant);

        var applied = model.TryApply(model.Initial, operation, out _);

        Assert.False(applied);
    }

    #endregion
}
