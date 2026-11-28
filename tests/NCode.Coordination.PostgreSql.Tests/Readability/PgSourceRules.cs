// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NCode.Coordination.PostgreSql.Tests.Readability;

/// <summary>
/// Parses the PostgreSQL provider's source and reports what breaks its readability rules: a call inside another
/// call's arguments, a lambda argument that isn't one plain expression, a member over 40 lines, a constant or
/// static member declared after an instance member, a member without a documentation comment, and a primary
/// constructor parameter read in a member instead of through a property.
/// </summary>
internal static class PgSourceRules
{
    private const string NestedCallRule = "nested-call";
    private const string NestedLambdaRule = "nested-lambda";
    private const string LongMemberRule = "long-member";
    private const string StaticOrderRule = "static-order";
    private const string MissingDocRule = "missing-doc";
    private const string CapturedParameterRule = "captured-parameter";

    private const int MaxMemberLines = 40;
    private const int MaxTextLength = 80;

    private const string SolutionFileName = "NCode.Coordination.slnx";

    /// <summary>
    /// Gets the folder of the PostgreSQL provider's package, <c>src/NCode.Coordination.PostgreSql</c> under the
    /// folder that holds the solution.
    /// </summary>
    internal static string PackageRoot { get; } =
        FindProjectRoot("src", "NCode.Coordination.PostgreSql");

    /// <summary>
    /// Gets the folder of the PostgreSQL provider's tests, <c>tests/NCode.Coordination.PostgreSql.Tests</c> under
    /// the folder that holds the solution.
    /// </summary>
    private static string TestRoot { get; } =
        FindProjectRoot("tests", "NCode.Coordination.PostgreSql.Tests");

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

        var nestedCalls = FindNestedCalls(root);
        var nestedLambdas = FindNestedLambdas(root);
        var longMembers = FindLongMembers(tree, root);

        var violations = new List<(SyntaxNode Node, string Rule)>();
        violations.AddRange(nestedCalls);
        violations.AddRange(nestedLambdas);
        violations.AddRange(longMembers);

        return DescribeAll(tree, violations);
    }

    /// <summary>
    /// Checks one source text for a constant or static member declared after an instance member, for a member
    /// without a documentation comment, and for a primary constructor parameter read in a member.
    /// </summary>
    /// <param name="source">The C# source text.</param>
    /// <returns>
    /// One <c>"line: rule: text"</c> per violation, ordered by position.
    /// </returns>
    public static ImmutableArray<string> CheckLayout(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var misplacedStatics = FindMisplacedStatics(root);
        var undocumentedMembers = FindUndocumentedMembers(root);
        var capturedParameters = FindCapturedParameters(root);

        var violations = new List<(SyntaxNode Node, string Rule)>();
        violations.AddRange(misplacedStatics);
        violations.AddRange(undocumentedMembers);
        violations.AddRange(capturedParameters);

        return DescribeAll(tree, violations);
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
    public static ImmutableArray<string> CheckPath(string path) =>
        CheckFiles(PackageRoot, path, CheckSource);

    private static ImmutableArray<string> CheckSource(string source)
    {
        var violations = Check(source);
        var layoutViolations = CheckLayout(source);
        return violations.AddRange(layoutViolations);
    }

    /// <summary>
    /// Checks the source files at a path in the PostgreSQL provider's tests, for a call inside another call's
    /// arguments only: a test's block lambda passed to an assertion and a long test body stay allowed.
    /// </summary>
    /// <param name="path">
    /// A <c>.cs</c> file, a folder (every <c>.cs</c> file under it), or <c>.</c> (the test project's own files),
    /// relative to <c>tests/NCode.Coordination.PostgreSql.Tests</c>.
    /// </param>
    /// <returns>
    /// The <c>nested-call</c> violations of every file, each prefixed with the file's path relative to the test
    /// project and a colon.
    /// </returns>
    public static ImmutableArray<string> CheckTestPath(string path) =>
        CheckFiles(TestRoot, path, CheckNestedCalls);

    private static ImmutableArray<string> CheckNestedCalls(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var nestedCalls = FindNestedCalls(root);

        var violations = new List<(SyntaxNode Node, string Rule)>();
        violations.AddRange(nestedCalls);

        return DescribeAll(tree, violations);
    }

    private static ImmutableArray<string> CheckFiles(
        string projectRoot,
        string path,
        Func<string, ImmutableArray<string>> checkSource
    )
    {
        var files = ResolveFiles(projectRoot, path);

        var violations = ImmutableArray.CreateBuilder<string>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            foreach (var violation in checkSource(source))
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

    #region Static order

    private static IEnumerable<(SyntaxNode Node, string Rule)> FindMisplacedStatics(SyntaxNode root)
    {
        var declarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
        var checkedDeclarations = declarations.Where(IsLayoutChecked);
        return checkedDeclarations.SelectMany(FindMisplacedStaticsIn);
    }

    private static bool IsLayoutChecked(TypeDeclarationSyntax declaration) =>
        declaration.Kind()
            is SyntaxKind.ClassDeclaration
                or SyntaxKind.StructDeclaration
                or SyntaxKind.RecordDeclaration
                or SyntaxKind.RecordStructDeclaration
                or SyntaxKind.InterfaceDeclaration;

    private static IEnumerable<(SyntaxNode Node, string Rule)> FindMisplacedStaticsIn(
        TypeDeclarationSyntax declaration
    )
    {
        var seenInstanceMember = false;
        foreach (var member in declaration.Members)
        {
            if (seenInstanceMember && IsConstantOrStatic(member))
                yield return (member, StaticOrderRule);

            if (IsInstanceMember(member))
                seenInstanceMember = true;
        }
    }

    private static bool IsConstantOrStatic(MemberDeclarationSyntax member) =>
        member switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ConstKeyword)
                || field.Modifiers.Any(SyntaxKind.StaticKeyword),
            EventFieldDeclarationSyntax
            or EventDeclarationSyntax
            or PropertyDeclarationSyntax
            or MethodDeclarationSyntax => member.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };

    private static bool IsInstanceMember(MemberDeclarationSyntax member) =>
        member switch
        {
            FieldDeclarationSyntax field => !field.Modifiers.Any(SyntaxKind.ConstKeyword)
                && !field.Modifiers.Any(SyntaxKind.StaticKeyword),
            EventFieldDeclarationSyntax
            or EventDeclarationSyntax
            or PropertyDeclarationSyntax
            or MethodDeclarationSyntax
            or ConstructorDeclarationSyntax => !member.Modifiers.Any(SyntaxKind.StaticKeyword),
            IndexerDeclarationSyntax => true,
            _ => false,
        };

    #endregion

    #region Missing documentation

    private static IEnumerable<(SyntaxNode Node, string Rule)> FindUndocumentedMembers(
        SyntaxNode root
    )
    {
        var nodes = root.DescendantNodes().Where(IsDocumentedKind);
        var undocumented = nodes.Where(node => !HasDocumentationComment(node));
        return undocumented.Select(node => (node, MissingDocRule));
    }

    private static bool IsDocumentedKind(SyntaxNode node) =>
        node switch
        {
            TypeDeclarationSyntax declaration => IsLayoutChecked(declaration),
            EnumDeclarationSyntax
            or DelegateDeclarationSyntax
            or EnumMemberDeclarationSyntax
            or BaseFieldDeclarationSyntax
            or PropertyDeclarationSyntax
            or EventDeclarationSyntax
            or IndexerDeclarationSyntax
            or MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or OperatorDeclarationSyntax
            or ConversionOperatorDeclarationSyntax => true,
            _ => false,
        };

    private static bool HasDocumentationComment(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia();
        return trivia.Any(SyntaxKind.SingleLineDocumentationCommentTrivia);
    }

    #endregion

    #region Captured parameters

    private static IEnumerable<(SyntaxNode Node, string Rule)> FindCapturedParameters(
        SyntaxNode root
    )
    {
        var declarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
        var parameterized = declarations.Where(HasPrimaryConstructor);
        var captured = parameterized.SelectMany(FindCapturedParametersIn);
        return captured.Select(identifier => ((SyntaxNode)identifier, CapturedParameterRule));
    }

    private static bool HasPrimaryConstructor(TypeDeclarationSyntax declaration) =>
        declaration.ParameterList is not null
        && declaration.Kind() is SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration;

    private static IEnumerable<IdentifierNameSyntax> FindCapturedParametersIn(
        TypeDeclarationSyntax declaration
    )
    {
        var parameters = declaration.ParameterList?.Parameters ?? default;
        var names = parameters.Select(parameter => parameter.Identifier.ValueText).ToHashSet();

        var identifiers = declaration.DescendantNodes().OfType<IdentifierNameSyntax>();
        var named = identifiers.Where(identifier =>
            names.Contains(identifier.Identifier.ValueText)
        );
        var read = named.Where(identifier => !IsNameOfAMemberOrArgument(identifier));
        return read.Where(identifier => IsReadInAMember(identifier, declaration));
    }

    private static bool IsNameOfAMemberOrArgument(IdentifierNameSyntax identifier) =>
        identifier.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Name == identifier,
            NameColonSyntax => true,
            _ => false,
        };

    private static bool IsReadInAMember(
        IdentifierNameSyntax identifier,
        TypeDeclarationSyntax declaration
    )
    {
        var owner = identifier.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (owner != declaration)
            return false;

        var members = identifier.Ancestors().OfType<MemberDeclarationSyntax>();
        var member = members.FirstOrDefault(candidate => candidate.Parent == declaration);
        return member switch
        {
            null => false,
            BaseFieldDeclarationSyntax => false,
            PropertyDeclarationSyntax property => !IsInInitializer(identifier, property),
            _ => true,
        };
    }

    private static bool IsInInitializer(
        IdentifierNameSyntax identifier,
        PropertyDeclarationSyntax property
    ) => property.Initializer is { } initializer && initializer.Span.Contains(identifier.Span);

    #endregion

    #region Reporting

    private static ImmutableArray<string> DescribeAll(
        SyntaxTree tree,
        List<(SyntaxNode Node, string Rule)> violations
    )
    {
        var ordered = violations.OrderBy(violation => violation.Node.SpanStart);
        return [.. ordered.Select(violation => Describe(tree, violation.Node, violation.Rule))];
    }

    private static string Describe(SyntaxTree tree, SyntaxNode node, string rule)
    {
        var span = tree.GetLineSpan(node.Span);
        var line = span.StartLinePosition.Line + 1;
        var text = node.ToString();
        var oneLine = ToOneLine(text);
        return $"{line}: {rule}: {oneLine}";
    }

    private static string ToOneLine(string text)
    {
        var words = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var oneLine = string.Join(' ', words);
        return oneLine.Length <= MaxTextLength ? oneLine : oneLine[..MaxTextLength];
    }

    #endregion

    #region Files

    private static string FindProjectRoot(string group, string project)
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

        return Path.Combine(directory.FullName, group, project);
    }

    private static bool ContainsSolution(DirectoryInfo directory)
    {
        var solutionPath = Path.Combine(directory.FullName, SolutionFileName);
        return File.Exists(solutionPath);
    }

    private static IEnumerable<string> ResolveFiles(string projectRoot, string path)
    {
        if (path == ".")
            return Directory.GetFiles(projectRoot, "*.cs", SearchOption.TopDirectoryOnly);

        var fullPath = Path.GetFullPath(path, projectRoot);
        if (fullPath.EndsWith(".cs", StringComparison.Ordinal) && File.Exists(fullPath))
            return [fullPath];

        if (Directory.Exists(fullPath))
            return GetFolderFiles(projectRoot, fullPath);

        throw new ArgumentException(
            $"'{path}' is not a .cs file or folder of the project.",
            nameof(path)
        );
    }

    private static IEnumerable<string> GetFolderFiles(string projectRoot, string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories);
        var sources = files.Where(file => !IsBuildOutput(projectRoot, file));
        return sources.Order(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string projectRoot, string file)
    {
        var relativePath = Path.GetRelativePath(projectRoot, file);
        var segments = relativePath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
        return segments.SkipLast(1).Any(segment => segment is "bin" or "obj");
    }

    #endregion
}
