// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCode.Coordination.PostgreSql.Tests.Readability;

/// <summary>
/// Parses the PostgreSQL provider's source and reports what breaks its readability rules: a call inside another
/// call's arguments, a lambda argument that isn't one plain expression, and a member over 40 lines.
/// </summary>
internal static class PgSourceRules
{
    private const string NestedCallRule = "nested-call";
    private const string NestedLambdaRule = "nested-lambda";
    private const string LongMemberRule = "long-member";

    private const int MaxMemberLines = 40;
    private const int MaxTextLength = 80;

    private const string SolutionFileName = "NCode.Coordination.slnx";

    private static string PackageRoot { get; } = FindPackageRoot();

    /// <summary>
    /// Checks one source text.
    /// </summary>
    /// <param name="source">The C# source text.</param>
    /// <returns>
    /// One <c>"line: rule: text"</c> per violation, ordered by position.
    /// </returns>
    public static ImmutableArray<string> Check(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var violations = new List<(SyntaxNode Node, string Rule)>();
        violations.AddRange(FindNestedCalls(root));
        violations.AddRange(FindNestedLambdas(root));
        violations.AddRange(FindLongMembers(tree, root));

        var ordered = violations.OrderBy(violation => violation.Node.SpanStart);
        return [.. ordered.Select(violation => Describe(tree, violation.Node, violation.Rule))];
    }

    /// <summary>
    /// Checks the source files at a path in the PostgreSQL provider's package.
    /// </summary>
    /// <param name="path">
    /// A <c>.cs</c> file, a folder (every <c>.cs</c> file under it), or <c>.</c> (the package root's own files),
    /// relative to <c>src/NCode.Coordination.PostgreSql</c>.
    /// </param>
    /// <returns>
    /// The violations of every file, each prefixed with the file's path relative to the package and a colon.
    /// </returns>
    public static ImmutableArray<string> CheckPath(string path)
    {
        var files = ResolveFiles(path);

        var violations = ImmutableArray.CreateBuilder<string>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(PackageRoot, file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            var fileViolations = Check(source);
            foreach (var violation in fileViolations)
            {
                violations.Add($"{relativePath}:{violation}");
            }
        }

        return violations.ToImmutable();
    }

    #region Nested calls

    private static IEnumerable<(SyntaxNode Node, string Rule)> FindNestedCalls(SyntaxNode root)
    {
        var arguments = root.DescendantNodes().OfType<ArgumentSyntax>();
        var nested = arguments.Where(IsCheckedArgument).Where(ContainsNestedCall);
        return nested.Select(argument => ((SyntaxNode)argument, NestedCallRule));
    }

    private static bool IsCheckedArgument(ArgumentSyntax argument) =>
        argument.Parent
            is ArgumentListSyntax
            {
                Parent: InvocationExpressionSyntax
                    or BaseObjectCreationExpressionSyntax
                    or ConstructorInitializerSyntax
                    or PrimaryConstructorBaseTypeSyntax,
            };

    private static bool ContainsNestedCall(ArgumentSyntax argument)
    {
        var descendants = argument.Expression.DescendantNodesAndSelf(IsNotAnonymousFunction);
        return descendants.Any(IsNestedCall);
    }

    private static bool IsNotAnonymousFunction(SyntaxNode node) =>
        node is not AnonymousFunctionExpressionSyntax;

    private static bool IsNestedCall(SyntaxNode node) =>
        node switch
        {
            InvocationExpressionSyntax invocation => !IsNameof(invocation),
            BaseObjectCreationExpressionSyntax => true,
            AwaitExpressionSyntax => true,
            _ => false,
        };

    private static bool IsNameof(InvocationExpressionSyntax invocation) =>
        invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" };

    #endregion

    #region Nested lambdas

    private static IEnumerable<(SyntaxNode Node, string Rule)> FindNestedLambdas(SyntaxNode root)
    {
        var lambdas = root.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>();
        var nested = lambdas.Where(IsInCheckedArgument).Where(IsNotPlainExpression);
        return nested.Select(lambda => ((SyntaxNode)lambda, NestedLambdaRule));
    }

    private static bool IsInCheckedArgument(AnonymousFunctionExpressionSyntax lambda)
    {
        var arguments = lambda.Ancestors().OfType<ArgumentSyntax>();
        return arguments.Any(IsCheckedArgument);
    }

    private static bool IsNotPlainExpression(AnonymousFunctionExpressionSyntax lambda)
    {
        if (lambda.Body is BlockSyntax)
            return true;

        var descendants = lambda.Body.DescendantNodesAndSelf();
        return descendants.OfType<AnonymousFunctionExpressionSyntax>().Any();
    }

    #endregion

    #region Long members

    private static IEnumerable<(SyntaxNode Node, string Rule)> FindLongMembers(
        SyntaxTree tree,
        SyntaxNode root
    )
    {
        var longMembers = root.DescendantNodes().Where(member => IsLong(tree, member));
        return longMembers.Select(member => (member, LongMemberRule));
    }

    private static bool IsLong(SyntaxTree tree, SyntaxNode member)
    {
        var body = GetBody(member);
        if (body is null)
            return false;

        var span = tree.GetLineSpan(body.Span);
        var lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        return lines > MaxMemberLines;
    }

    private static SyntaxNode? GetBody(SyntaxNode member) =>
        member switch
        {
            DestructorDeclarationSyntax => null,
            BaseMethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody,
            AccessorDeclarationSyntax accessor => accessor.Body
                ?? (SyntaxNode?)accessor.ExpressionBody,
            LocalFunctionStatementSyntax local => local.Body ?? (SyntaxNode?)local.ExpressionBody,
            _ => null,
        };

    #endregion

    #region Reporting

    private static string Describe(SyntaxTree tree, SyntaxNode node, string rule)
    {
        var span = tree.GetLineSpan(node.Span);
        var line = span.StartLinePosition.Line + 1;
        var text = ToOneLine(node.ToString());
        return $"{line}: {rule}: {text}";
    }

    private static string ToOneLine(string text)
    {
        var words = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var oneLine = string.Join(' ', words);
        return oneLine.Length <= MaxTextLength ? oneLine : oneLine[..MaxTextLength];
    }

    #endregion

    #region Files

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

    private static IEnumerable<string> ResolveFiles(string path)
    {
        if (path == ".")
            return Directory.GetFiles(PackageRoot, "*.cs", SearchOption.TopDirectoryOnly);

        var fullPath = Path.GetFullPath(path, PackageRoot);
        if (fullPath.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(fullPath))
            return [fullPath];

        if (Directory.Exists(fullPath))
            return GetFolderFiles(fullPath);

        throw new ArgumentException(
            $"'{path}' is not a .cs file or folder of the package.",
            nameof(path)
        );
    }

    private static IEnumerable<string> GetFolderFiles(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories);
        var sources = files.Where(file => !IsBuildOutput(file));
        return sources.Order(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string file)
    {
        var relativePath = Path.GetRelativePath(PackageRoot, file);
        var segments = relativePath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
        return segments.SkipLast(1).Any(segment => segment is "bin" or "obj");
    }

    #endregion
}
