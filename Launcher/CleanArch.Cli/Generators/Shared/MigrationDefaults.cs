using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CleanArch.Cli.Generators.Shared;

internal static class MigrationDefaults
{
    internal sealed record Result(string? Text, bool Changed, string? Problem);

    internal static Result Apply(string text, string table, IReadOnlyList<Field> fields)
    {
        var root = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        var up = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(method => method.Identifier.Text == "Up");

        if (up is null)
        {
            return new Result(null, false, "the migration declares no Up method, where its AddColumn calls are.");
        }

        var replacements = new Dictionary<SyntaxNode, SyntaxNode>();

        foreach (var field in fields.Where(field => field.Default is not null))
        {
            var calls = up.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(call => call.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "AddColumn" })
                .Where(call => Argument(call, "name") == field.Name && Argument(call, "table") == table)
                .ToList();

            if (calls.Count != 1)
            {
                return new Result(null, false, $"the migration's Up has {calls.Count} AddColumn of {field.Name} to {table}, where new-field sets its defaultValue in one.");
            }

            var call = calls[0];
            var value = SyntaxFactory.ParseExpression(field.Default!.Expression);
            var present = call.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.Text == "defaultValue");

            if (present is not null && SyntaxMerge.Normalized(present.Expression) == SyntaxMerge.Normalized(value))
            {
                continue;
            }

            var arguments = call.ArgumentList.Arguments;

            replacements[call.ArgumentList] = call.ArgumentList.WithArguments(present is not null
                ? arguments.Replace(present, present.WithExpression(value.WithTriviaFrom(present.Expression)))
                : SyntaxFactory.SeparatedList(
                    [.. arguments, SyntaxFactory.Argument(
                        SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("defaultValue"), SyntaxFactory.Token(SyntaxKind.ColonToken).WithTrailingTrivia(SyntaxFactory.Space)),
                        default,
                        value).WithLeadingTrivia(arguments.Last().GetLeadingTrivia())],
                    [.. arguments.GetSeparators(), SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"))]));
        }

        return replacements.Count == 0
            ? new Result(text, false, null)
            : new Result(root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]).ToFullString(), true, null);
    }

    private static string? Argument(InvocationExpressionSyntax call, string name) =>
        call.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.Text == name)?.Expression is LiteralExpressionSyntax literal
            ? literal.Token.ValueText
            : null;
}
