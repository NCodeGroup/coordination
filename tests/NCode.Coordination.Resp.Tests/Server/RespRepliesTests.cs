// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server.Tests;

public sealed class RespRepliesTests
{
    #region Script Tests

    [Fact]
    public void Script_StatusAndFields_ReturnsThem()
    {
        var reply = RedisResult.Create([
            RedisResult.Create((RedisValue)1, ResultType.Integer),
            RedisResult.Create((RedisValue)7, ResultType.Integer),
            RedisResult.Create((RedisValue)"text"),
        ]);

        var (status, fields) = RespReplies.Script(reply);

        Assert.Equal(1, status);
        Assert.Equal(2, fields.Length);
        Assert.Equal(7, (long)fields[0]);
        Assert.Equal("text", (string?)fields[1]);
    }

    [Fact]
    public void Script_NotAnArray_ThrowsUnavailableWithOutcomeUnknown()
    {
        var reply = RedisResult.Create((RedisValue)42, ResultType.Integer);

        var exception = Assert.Throws<CoordinationException>(() => RespReplies.Script(reply));

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
        Assert.True(exception.OutcomeUnknown);
    }

    [Fact]
    public void Script_EmptyArray_ThrowsUnavailable()
    {
        var reply = RedisResult.Create(Array.Empty<RedisResult>());

        var exception = Assert.Throws<CoordinationException>(() => RespReplies.Script(reply));

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    [Fact]
    public void Script_StatusNotAnInteger_ThrowsUnavailable()
    {
        var reply = RedisResult.Create([RedisResult.Create((RedisValue)"text")]);

        var exception = Assert.Throws<CoordinationException>(() => RespReplies.Script(reply));

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    #endregion

    #region Integer Tests

    [Fact]
    public void Integer_IntegerReply_ReturnsIt()
    {
        var reply = RedisResult.Create((RedisValue)42, ResultType.Integer);

        var result = RespReplies.Integer(reply);

        Assert.Equal(42, result);
    }

    [Fact]
    public void Integer_BulkStringOfDigits_ParsesIt()
    {
        var reply = RedisResult.Create((RedisValue)"42");

        var result = RespReplies.Integer(reply);

        Assert.Equal(42, result);
    }

    [Fact]
    public void Integer_BulkStringOfText_ThrowsUnavailable()
    {
        var reply = RedisResult.Create((RedisValue)"forty-two");

        var exception = Assert.Throws<CoordinationException>(() => RespReplies.Integer(reply));

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    [Fact]
    public void Integer_NullReply_ThrowsUnavailable()
    {
        var reply = RedisResult.Create(RedisValue.Null, ResultType.BulkString);

        var exception = Assert.Throws<CoordinationException>(() => RespReplies.Integer(reply));

        Assert.Equal(CoordinationErrorCodes.Unavailable, exception.Code);
    }

    #endregion
}
