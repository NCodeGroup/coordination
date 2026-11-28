// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.PostgreSql.Tests.Readability;

public sealed class PgSourceRulesTests
{
    private const string SolutionFileName = "NCode.Coordination.slnx";

    public static TheoryData<string> CheckedPaths
    {
        get
        {
            var data = new TheoryData<string> { "." };
            foreach (var folder in FindPackageFolders())
            {
                data.Add(folder);
            }

            return data;
        }
    }

    private static List<string> FindPackageFolders()
    {
        var packageRoot = FindPackageRoot();
        var folders = Directory.GetDirectories(packageRoot);
        var names = folders.Select(folder => Path.GetFileName(folder));
        var sources = names.Where(name => name is not ("bin" or "obj"));
        return sources.Order(StringComparer.Ordinal).ToList();
    }

    private static string FindPackageRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !ContainsSolution(directory))
        {
            directory = directory.Parent;
        }

        if (directory is null)
            throw new InvalidOperationException(
                $"No folder above the tests holds {SolutionFileName}."
            );

        return Path.Combine(directory.FullName, "src", "NCode.Coordination.PostgreSql");
    }

    private static bool ContainsSolution(DirectoryInfo directory)
    {
        var solutionPath = Path.Combine(directory.FullName, SolutionFileName);
        return File.Exists(solutionPath);
    }

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

    #region CheckLayout Tests

    [Fact]
    public void CheckLayout_StaticMethodAfterAnInstanceMethod_ReportsStaticOrder()
    {
        const string source = """
            /// <summary>C.</summary>
            class C
            {
                /// <summary>A.</summary>
                void A() { }

                /// <summary>B.</summary>
                static void B() { }
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Equal<string>(["8: static-order: static void B() { }"], violations);
    }

    [Fact]
    public void CheckLayout_ConstAfterAProperty_ReportsStaticOrder()
    {
        const string source = """
            /// <summary>C.</summary>
            class C
            {
                /// <summary>P.</summary>
                int P { get; }

                /// <summary>K.</summary>
                const int K = 1;
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Equal<string>(["8: static-order: const int K = 1;"], violations);
    }

    [Fact]
    public void CheckLayout_StaticsBeforeInstanceMembers_ReportsNothing()
    {
        const string source = """
            /// <summary>C.</summary>
            class C
            {
                /// <summary>K.</summary>
                const int K = 1;

                /// <summary>F.</summary>
                static int F;

                /// <summary>B.</summary>
                static void B() { }

                /// <summary>P.</summary>
                int P { get; }

                /// <summary>M.</summary>
                void M() { }
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void CheckLayout_MemberWithoutDocComment_ReportsMissingDoc()
    {
        const string source = """
            /// <summary>C.</summary>
            class C
            {
                void M() { }
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Equal<string>(["4: missing-doc: void M() { }"], violations);
    }

    [Fact]
    public void CheckLayout_InheritdocMember_ReportsNothing()
    {
        const string source = """
            /// <summary>C.</summary>
            class C
            {
                /// <inheritdoc />
                void M() { }
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void CheckLayout_ParameterReadInAMethod_ReportsCapturedParameter()
    {
        const string source = """
            /// <summary>C.</summary>
            class C(int value)
            {
                /// <summary>M.</summary>
                int M() => value;
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Equal<string>(["5: captured-parameter: value"], violations);
    }

    [Fact]
    public void CheckLayout_ParameterReadInAPropertyInitializer_ReportsNothing()
    {
        const string source = """
            /// <summary>C.</summary>
            class C(int value)
            {
                /// <summary>P.</summary>
                int P { get; } = value;

                /// <summary>M.</summary>
                int M() => P;
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Empty(violations);
    }

    [Fact]
    public void CheckLayout_RecordParameterReadInAMethod_ReportsNothing()
    {
        const string source = """
            /// <summary>C.</summary>
            record C(int Value)
            {
                /// <summary>M.</summary>
                int M() => Value;
            }
            """;

        var violations = PgSourceRules.CheckLayout(source);

        Assert.Empty(violations);
    }

    #endregion

    #region CheckPath Tests

    [Theory]
    [MemberData(nameof(CheckedPaths))]
    public void CheckPath_CheckedPath_FollowsTheRules(string path)
    {
        var violations = PgSourceRules.CheckPath(path);

        Assert.True(violations.IsEmpty, string.Join(Environment.NewLine, violations));
    }

    #endregion
}
