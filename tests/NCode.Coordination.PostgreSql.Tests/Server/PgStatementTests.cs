// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgStatementTests
{
    #region ToBatchCommand Tests

    [Fact]
    public void ToBatchCommand_Arguments_AddsParametersInOrder()
    {
        var statement = new PgStatement(
            "SELECT $1, $2, $3",
            PgArgument.Of(7L),
            PgArgument.Typed("text", NpgsqlDbType.Text),
            PgArgument.Typed(new byte[] { 1, 2 }, NpgsqlDbType.Bytea)
        );

        var command = statement.ToBatchCommand();

        Assert.Equal("SELECT $1, $2, $3", command.CommandText);
        Assert.Equal(3, command.Parameters.Count);
        Assert.Equal(7L, command.Parameters[0].Value);
        Assert.Equal("text", command.Parameters[1].Value);
        Assert.Equal(NpgsqlDbType.Text, command.Parameters[1].NpgsqlDbType);
        Assert.Equal(new byte[] { 1, 2 }, command.Parameters[2].Value);
        Assert.Equal(NpgsqlDbType.Bytea, command.Parameters[2].NpgsqlDbType);
        Assert.Equal(3, statement.Arguments.Length);
    }

    #endregion

    #region Typed Tests

    [Fact]
    public void Typed_Null_GivesDbNullWithItsType()
    {
        var argument = PgArgument.Typed(null, NpgsqlDbType.Bigint);

        Assert.Equal(DBNull.Value, argument.Value);
        Assert.Equal(NpgsqlDbType.Bigint, argument.Type);

        var parameter = argument.ToParameter();

        Assert.Equal(DBNull.Value, parameter.Value);
        Assert.Equal(NpgsqlDbType.Bigint, parameter.NpgsqlDbType);
    }

    #endregion

    #region Of Tests

    [Fact]
    public void Of_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PgArgument.Of(null!));
    }

    #endregion

    #region ImplicitConversion Tests

    [Fact]
    public void ImplicitConversion_Long_GivesTheArgumentOfBuilds()
    {
        PgArgument argument = 7L;

        Assert.Equal(PgArgument.Of(7L), argument);
    }

    [Fact]
    public void ImplicitConversion_String_GivesTheArgumentOfBuilds()
    {
        PgArgument argument = "text";

        Assert.Equal(PgArgument.Of("text"), argument);
    }

    [Fact]
    public void ImplicitConversion_Bytes_GivesTheArgumentOfBuilds()
    {
        var bytes = new byte[] { 1, 2 };
        PgArgument argument = bytes;

        Assert.Equal(PgArgument.Of(bytes), argument);
    }

    [Fact]
    public void ImplicitConversion_NullString_Throws()
    {
        string text = null!;

        Assert.Throws<ArgumentNullException>(() => (PgArgument)text);
    }

    #endregion
}
