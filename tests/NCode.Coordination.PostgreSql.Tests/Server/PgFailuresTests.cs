// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgFailuresTests
{
    private static PostgresException CreateServerError(string state, string severity)
    {
        return new PostgresException("failed", severity, severity, state);
    }

    #region Translate Tests

    [Theory]
    [InlineData("08006", "FATAL", true, "unavailable", true)]
    [InlineData("40001", "ERROR", true, "unavailable", false)]
    [InlineData("57014", "ERROR", true, "unavailable", false)]
    [InlineData("57P01", "FATAL", false, "unavailable", false)]
    [InlineData("57P01", "FATAL", true, "unavailable", true)]
    [InlineData("53300", "FATAL", false, "unavailable", false)]
    [InlineData("55P03", "ERROR", true, "unavailable", false)]
    [InlineData("25P03", "FATAL", true, "unavailable", false)]
    [InlineData("25P04", "ERROR", true, "unavailable", false)]
    [InlineData("08007", "ERROR", true, "unavailable", true)]
    [InlineData("28P01", "FATAL", false, "access-denied", false)]
    [InlineData("42501", "ERROR", true, "access-denied", false)]
    [InlineData("22003", "ERROR", true, "limit-exceeded", false)]
    [InlineData("22012", "ERROR", true, "unavailable", false)]
    public void Translate_ServerError_CodesAndOutcome(
        string state,
        string severity,
        bool committingStepSent,
        string expectedCode,
        bool expectedUnknown
    )
    {
        var exception = CreateServerError(state, severity);

        var result = PgFailures.Translate(exception, committingStepSent);

        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(expectedUnknown, result.OutcomeUnknown);
        Assert.Same(exception, result.InnerException);
        Assert.Equal($"The PostgreSQL server call failed: {state} failed", result.Message);
    }

    [Fact]
    public void Translate_NetworkFailureAfterCommit_IsUnknown()
    {
        var exception = new NpgsqlException("down");

        var result = PgFailures.Translate(exception, committingStepSent: true);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.True(result.OutcomeUnknown);
        Assert.Same(exception, result.InnerException);
        Assert.Equal("The PostgreSQL server call failed: down", result.Message);
    }

    [Fact]
    public void Translate_NetworkFailureBeforeCommit_IsKnown()
    {
        var exception = new NpgsqlException("down");

        var result = PgFailures.Translate(exception, committingStepSent: false);

        Assert.Equal(CoordinationErrorCodes.Unavailable, result.Code);
        Assert.False(result.OutcomeUnknown);
    }

    #endregion

    #region IsKeyspaceMissing Tests

    [Fact]
    public void IsKeyspaceMissing_ForeignKeyViolation_ReturnsTrue()
    {
        var exception = CreateServerError("23503", "ERROR");

        var missing = PgFailures.IsKeyspaceMissing(exception);

        Assert.True(missing);
    }

    [Fact]
    public void IsKeyspaceMissing_OtherError_ReturnsFalse()
    {
        var exception = CreateServerError("23505", "ERROR");

        var missing = PgFailures.IsKeyspaceMissing(exception);

        Assert.False(missing);
    }

    #endregion

    #region IsClientFailure Tests

    [Fact]
    public void IsClientFailure_NpgsqlExceptions_ReturnsTrue()
    {
        var clientError = new NpgsqlException("down");
        var serverError = CreateServerError("40001", "ERROR");

        var clientIsFailure = PgFailures.IsClientFailure(clientError);
        var serverIsFailure = PgFailures.IsClientFailure(serverError);

        Assert.True(clientIsFailure);
        Assert.True(serverIsFailure);
    }

    [Fact]
    public void IsClientFailure_OtherException_ReturnsFalse()
    {
        var exception = new InvalidOperationException();

        var isFailure = PgFailures.IsClientFailure(exception);

        Assert.False(isFailure);
    }

    #endregion

    #region IsConnectionLost Tests

    public static TheoryData<Exception> LostConnectionFailures =>
        [
            CreateServerError("57P01", "FATAL"),
            CreateServerError("XX000", "PANIC"),
            new NpgsqlException("down"),
        ];

    public static TheoryData<Exception> StatementFailures =>
        [
            CreateServerError("57014", "ERROR"),
            CreateServerError("23505", "ERROR"),
            new OperationCanceledException(),
        ];

    [Theory]
    [MemberData(nameof(LostConnectionFailures))]
    public void IsConnectionLost_ServerEndedOrIoFailure_ReturnsTrue(Exception exception)
    {
        var lost = PgFailures.IsConnectionLost(exception);

        Assert.True(lost);
    }

    [Theory]
    [MemberData(nameof(StatementFailures))]
    public void IsConnectionLost_StatementError_ReturnsFalse(Exception exception)
    {
        var lost = PgFailures.IsConnectionLost(exception);

        Assert.False(lost);
    }

    #endregion
}
