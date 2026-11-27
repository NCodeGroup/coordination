// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NpgsqlTypes;

namespace NCode.Coordination.PostgreSql.Server.Tests;

public sealed class PgStatementTests
{
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

    [Fact]
    public void Of_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PgArgument.Of(null!));
    }
}
