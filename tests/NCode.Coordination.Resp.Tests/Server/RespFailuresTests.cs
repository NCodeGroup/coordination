// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespFailuresTests
{
    #region Translate Tests

    [Fact]
    public void Translate_ConnectionFailureWaitingToBeSent_IsUnavailableAndKnown()
    {
        var exception = new RedisConnectionException(
            ConnectionFailureType.SocketFailure,
            CommandFlags.None,
            "x",
            null,
            CommandStatus.WaitingToBeSent
        );

        var result = RespFailures.Translate(exception, isScript: false);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.False(result.OutcomeUnknown);
        Assert.Same(exception, result.InnerException);
        Assert.Equal("The RESP server call failed: x", result.Message);
    }

    [Fact]
    public void Translate_ConnectionFailureSent_IsUnavailableAndUnknown()
    {
        var exception = new RedisConnectionException(
            ConnectionFailureType.SocketFailure,
            CommandFlags.None,
            "x",
            null,
            CommandStatus.Sent
        );

        var result = RespFailures.Translate(exception, isScript: false);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.True(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_AuthenticationFailure_IsAccessDenied()
    {
        var exception = new RedisConnectionException(
            ConnectionFailureType.AuthenticationFailure,
            CommandFlags.None,
            "x",
            null,
            CommandStatus.Sent
        );

        var result = RespFailures.Translate(exception, isScript: false);

        Assert.Equal(CoordinationErrorCodes.AccessDenied, result.Code);
        Assert.False(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_TimeoutWaitingInBacklog_IsUnavailableAndKnown()
    {
        var exception = new RedisTimeoutException(
            CommandFlags.None,
            "x",
            CommandStatus.WaitingInBacklog
        );

        var result = RespFailures.Translate(exception, isScript: false);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.False(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_TimeoutSent_IsUnavailableAndUnknown()
    {
        var exception = new RedisTimeoutException(CommandFlags.None, "x", CommandStatus.Sent);

        var result = RespFailures.Translate(exception, isScript: false);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.True(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_NoPermReply_IsAccessDeniedAndKnown()
    {
        var exception = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "NOPERM no"
        );

        var result = RespFailures.Translate(exception, isScript: true);

        Assert.Equal(CoordinationErrorCodes.AccessDenied, result.Code);
        Assert.False(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_UnknownReplyFromScript_IsUnknown()
    {
        var exception = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "ERR ERR stop"
        );

        var result = RespFailures.Translate(exception, isScript: true);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.True(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_UnknownReplyFromCommand_IsKnown()
    {
        var exception = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "ERR ERR stop"
        );

        var result = RespFailures.Translate(exception, isScript: false);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.False(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_LoadingReplyFromScript_IsKnown()
    {
        var exception = new RedisServerException(
            RedisErrorKind.Unknown,
            CommandFlags.None,
            "LOADING x"
        );

        var result = RespFailures.Translate(exception, isScript: true);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.False(result.OutcomeUnknown);
    }

    [Fact]
    public void Translate_OtherException_IsUnavailableAndUnknown()
    {
        var exception = new InvalidOperationException("x");

        var result = RespFailures.Translate(exception, isScript: false);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.True(result.OutcomeUnknown);
    }

    #endregion

    #region IsClientFailure Tests

    [Fact]
    public void IsClientFailure_TimeoutException_IsTrue()
    {
        var exception = new RedisTimeoutException(CommandFlags.None, "x", CommandStatus.Sent);

        var result = RespFailures.IsClientFailure(exception);

        Assert.True(result);
    }

    [Fact]
    public void IsClientFailure_OtherException_IsFalse()
    {
        var exception = new InvalidOperationException("x");

        var result = RespFailures.IsClientFailure(exception);

        Assert.False(result);
    }

    #endregion

    #region ErrorToken Tests

    [Theory]
    [InlineData("ERR ERR stop", "stop")]
    [InlineData("ERR stop now", "stop")]
    [InlineData("ERR", "ERR")]
    [InlineData("", "ERR")]
    [InlineData("NOPERM no", "NOPERM")]
    public void ErrorToken_Message_ReturnsFirstWordAfterErrPrefixes(string message, string expected)
    {
        var token = RespFailures.ErrorToken(message);

        Assert.Equal(expected, token);
    }

    #endregion
}
