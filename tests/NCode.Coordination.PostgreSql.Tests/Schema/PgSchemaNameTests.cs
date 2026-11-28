// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
        var expected = $"'{name}' is not a schema name the provider takes";

        var exception = Assert.Throws<ArgumentException>(() => PgSchemaName.Create(name));

        Assert.Equal("name", exception.ParamName);
        Assert.StartsWith(expected, exception.Message, StringComparison.Ordinal);
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

    #region Hash Property Tests

    [Fact]
    public void Hash_DefaultName_IsTheKnownDigestPrefix()
    {
        // The first eight hex digits of cbbeb3cc2d482a2ffd900c233cc3bc5a84f2770c9b2f546a5041e35d8216048c,
        // the SHA-256 of "coordination".
        const string expected = "cbbeb3cc";
        var schema = PgSchemaName.Create("coordination");

        var hash = schema.Hash;

        Assert.Equal(expected, hash);
    }

    #endregion
}
