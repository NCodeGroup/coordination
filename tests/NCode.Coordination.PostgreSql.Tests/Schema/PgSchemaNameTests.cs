// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;

namespace NCode.Coordination.PostgreSql.Schema.Tests;

public sealed class PgSchemaNameTests
{
    #region Create Tests

    [Theory]
    [InlineData("coordination")]
    [InlineData("_x1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Create_ValidName_QuotesIt(string name)
    {
        var schema = PgSchemaName.Create(name);

        Assert.Equal(name, schema.Name);
        Assert.Equal("\"" + name + "\"", schema.Quoted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Coordination")]
    [InlineData("1abc")]
    [InlineData("a-b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Create_InvalidName_Throws(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => PgSchemaName.Create(name));

        Assert.Equal("name", exception.ParamName);
    }

    #endregion

    #region Apply Tests

    [Fact]
    public void Apply_Template_ReplacesEveryPlaceholder()
    {
        var schema = PgSchemaName.Create("ncode");

        var sql = schema.Apply("SELECT 1 FROM {schema}.a JOIN {schema}.b");

        Assert.Equal("SELECT 1 FROM \"ncode\".a JOIN \"ncode\".b", sql);
    }

    #endregion

    #region Hash Tests

    [Fact]
    public void Hash_Name_IsEightLowercaseHexDigits()
    {
        var schema = PgSchemaName.Create("coordination");

        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("coordination"))
        )[..8];

        Assert.Equal(8, schema.Hash.Length);
        Assert.Equal(expected, schema.Hash);
    }

    #endregion
}
