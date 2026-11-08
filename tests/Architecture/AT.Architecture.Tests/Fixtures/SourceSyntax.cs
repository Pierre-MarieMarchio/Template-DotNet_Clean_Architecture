using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class SourceSyntax
{
    private const string _attributeSuffix = "Attribute";

    internal static IEnumerable<string> DeclarationsCarrying(string source, string attribute) =>
        Parse(source)
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(member => member.AttributeLists
                .SelectMany(list => list.Attributes)
                .Any(carried => IsAttributeNamed(carried, attribute)))
            .Select(DeclaredName)
            .OfType<string>();

    internal static int InvocationsOf(string source, string member) =>
        Parse(source)
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(invocation => string.Equals(InvokedName(invocation.Expression), member, StringComparison.Ordinal));

    internal static IEnumerable<string> NamesUsedInCode(string source) =>
        Parse(source)
            .DescendantNodes(node => node is not UsingDirectiveSyntax && !IsNameof(node))
            .OfType<SimpleNameSyntax>()
            .Select(name => name.Identifier.ValueText);

    private static SyntaxNode Parse(string source) => CSharpSyntaxTree.ParseText(source).GetRoot();

    private static bool IsAttributeNamed(AttributeSyntax carried, string attribute)
    {
        string name = RightmostName(carried.Name);

        return string.Equals(name, attribute, StringComparison.Ordinal)
            || string.Equals(name, attribute + _attributeSuffix, StringComparison.Ordinal);
    }

    private static string RightmostName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => name.ToString(),
    };

    private static string? DeclaredName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        _ => null,
    };

    private static string? InvokedName(ExpressionSyntax invoked) => invoked switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => null,
    };

    private static bool IsNameof(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } };
}
