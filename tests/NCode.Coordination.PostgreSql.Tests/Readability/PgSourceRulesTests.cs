// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Tests.Readability;

public sealed class PgSourceRulesTests
{
    public static TheoryData<string> CheckedPaths =>
        [
            "Server",
            "Keys",
            "Schema",
            ".",
            "Counters",
            "Occurrences",
            "Sequences",
            "Sets",
            "Claims",
            "Records",
            "Transactions",
            "Sessions",
            "Elections",
            "Leases",
            "Membership",
            "Notifications",
            "Subscriptions",
            "Sweep",
        ];

    private static string BuildMethodSpanning(int lines)
    {
        var statements = Enumerable.Repeat("        Run();", lines - 2);
        var body = string.Join('\n', statements);
        return $"class C\n{{\n    void M()\n    {{\n{body}\n    }}\n}}";
    }

    #region Check Tests

    [Fact]
    public void Check_CallInAnArgument_ReportsNestedCall()
    {
        const string source = "class C { void M() { Foo(Bar()); } }";

        var violations = PgSourceRules.Check(source);

        Assert.Equal<string>(["1: nested-call: Bar()"], violations);
    }

    [Fact]
    public void Check_NameofInAnArgument_ReportsNothing()
    {
        const string source = "class C { void M() { Foo(nameof(C)); } }";

        var violations = PgSourceRules.Check(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_AwaitInAnArgument_ReportsNestedCall()
    {
        const string source = "class C { async void M() { Foo(await task); } }";

        var violations = PgSourceRules.Check(source);

        Assert.Equal<string>(["1: nested-call: await task"], violations);
    }

    [Fact]
    public void Check_ObjectCreationInACollectionArgument_ReportsNestedCall()
    {
        const string source = "class C { void M() { Foo([new X()]); } }";

        var violations = PgSourceRules.Check(source);

        Assert.Equal<string>(["1: nested-call: [new X()]"], violations);
    }

    [Fact]
    public void Check_CallInsideALambdaArgument_ChecksOnlyTheLambdasOwnArguments()
    {
        const string plain = "class C { void M() { Run(x => Foo(x)); } }";
        const string nested = "class C { void M() { Run(x => Foo(Bar(x))); } }";

        var plainViolations = PgSourceRules.Check(plain);
        var nestedViolations = PgSourceRules.Check(nested);

        Assert.Empty(plainViolations);
        Assert.Equal<string>(["1: nested-call: Bar(x)"], nestedViolations);
    }

    [Fact]
    public void Check_BlockLambdaArgument_ReportsNestedLambda()
    {
        const string source = "class C { void M() { Run(x => { return x; }); } }";

        var violations = PgSourceRules.Check(source);

        Assert.Equal<string>(["1: nested-lambda: x => { return x; }"], violations);
    }

    [Fact]
    public void Check_LambdaInsideALambdaArgument_ReportsNestedLambda()
    {
        const string source = "class C { void M() { Run(x => Foo(y => y)); } }";

        var violations = PgSourceRules.Check(source);

        Assert.Equal<string>(["1: nested-lambda: x => Foo(y => y)"], violations);
    }

    [Fact]
    public void Check_MethodGroupArgument_ReportsNothing()
    {
        const string source = "class C { void M() { Run(Handle); Run(this.Handle); } }";

        var violations = PgSourceRules.Check(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void Check_MemberOfFortyOneLines_ReportsLongMember()
    {
        var source = BuildMethodSpanning(41);

        var violations = PgSourceRules.Check(source);

        var violation = Assert.Single(violations);
        Assert.StartsWith("3: long-member: void M()", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_MemberOfFortyLines_ReportsNothing()
    {
        var source = BuildMethodSpanning(40);

        var violations = PgSourceRules.Check(source);

        Assert.Empty(violations);
    }

    #endregion

    #region CheckPath Tests

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CheckedPaths))]
    public void CheckPath_CheckedPath_FollowsTheRules(string path)
    {
        var violations = PgSourceRules.CheckPath(path);

        Assert.True(violations.IsEmpty, string.Join(Environment.NewLine, violations));
    }

    #endregion
}
