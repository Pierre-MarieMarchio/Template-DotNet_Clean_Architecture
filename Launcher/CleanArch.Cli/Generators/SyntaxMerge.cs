using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CleanArch.Cli.Generators;

internal static class SyntaxMerge
{
    internal sealed record Outcome(byte[]? Bytes, List<string> Changes, List<string> Problems);

    private static readonly CSharpParseOptions _options = new(LanguageVersion.Preview);

    internal static Outcome Merge(string path, string before, string after, byte[] disk)
    {
        var changes = new List<string>();
        var problems = new List<string>();
        bool bom = disk.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        string text = Encoding.UTF8.GetString(disk.AsSpan(bom ? 3 : 0));
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var tree = CSharpSyntaxTree.ParseText(text, _options);

        if (tree.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) is { } failure)
        {
            problems.Add(
                $"{path} does not parse, at line {failure.Location.GetLineSpan().StartLinePosition.Line + 1}: {failure.GetMessage(CultureInfo.InvariantCulture)} "
                + "Its members cannot be found until it does.");

            return new Outcome(null, changes, problems);
        }

        var merger = new Merger(path, changes, problems);
        var merged = merger.Unit(Parse(before), Parse(after), (CompilationUnitSyntax)tree.GetRoot());

        if (problems.Count > 0 || merged is null)
        {
            return new Outcome(null, changes, problems);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(merged.ToFullString().ReplaceLineEndings(newline));

        return new Outcome(bom ? [.. Encoding.UTF8.Preamble, .. bytes] : bytes, changes, problems);
    }

    private static CompilationUnitSyntax Parse(string text) => (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, _options).GetRoot();

    internal static string Normalized(SyntaxNode node) => node.NormalizeWhitespace("    ", "\n").ToFullString().Trim();

    private static bool Same(SyntaxNode left, SyntaxNode right) => Normalized(left) == Normalized(right);

    private sealed class Merger(string path, List<string> changes, List<string> problems)
    {
        internal CompilationUnitSyntax? Unit(CompilationUnitSyntax old, CompilationUnitSyntax @new, CompilationUnitSyntax disk)
        {
            var withUsings = Usings(old, @new, disk);
            var members = Members(string.Empty, old.Members, @new.Members, withUsings.Members);

            return members is null ? null : withUsings.WithMembers(members.Value);
        }

        private CompilationUnitSyntax Usings(CompilationUnitSyntax old, CompilationUnitSyntax @new, CompilationUnitSyntax disk)
        {
            static string Key(UsingDirectiveSyntax directive) => Normalized(directive.WithoutTrivia());

            var oldKeys = old.Usings.Select(Key).ToHashSet(StringComparer.Ordinal);
            var newKeys = @new.Usings.Select(Key).ToHashSet(StringComparer.Ordinal);
            var usings = disk.Usings.ToList();
            bool hadNone = usings.Count == 0;

            foreach (string removed in oldKeys.Where(key => !newKeys.Contains(key)))
            {
                if (usings.FindIndex(directive => Key(directive) == removed) is var index and >= 0)
                {
                    usings.RemoveAt(index);
                    changes.Add($"removed {removed}");
                }
            }

            foreach (var directive in @new.Usings.Where(directive => !oldKeys.Contains(Key(directive))))
            {
                if (usings.Any(present => Key(present) == Key(directive)))
                {
                    continue;
                }

                int following = usings.FindIndex(present => Order(directive.NamespaceOrType?.ToString() ?? string.Empty, present.NamespaceOrType?.ToString() ?? string.Empty) < 0);

                usings.Insert(following < 0 ? usings.Count : following, directive.WithLeadingTrivia().WithTrailingTrivia(SyntaxFactory.EndOfLine("\n")));
                changes.Add($"added {Key(directive)}");
            }

            var updated = disk.WithUsings(SyntaxFactory.List(usings));

            if (hadNone && usings.Count > 0 && updated.Members.FirstOrDefault() is { } first)
            {
                updated = updated.ReplaceNode(first, first.WithLeadingTrivia(first.GetLeadingTrivia().Insert(0, SyntaxFactory.EndOfLine("\n"))));
            }

            return updated;
        }

        private static int Order(string left, string right)
        {
            static bool IsSystem(string space) => space == "System" || space.StartsWith("System.", StringComparison.Ordinal);

            int system = IsSystem(right).CompareTo(IsSystem(left));

            return system != 0 ? system
                : StringComparer.OrdinalIgnoreCase.Compare(left, right) is var ignoringCase and not 0 ? ignoringCase
                : StringComparer.Ordinal.Compare(left, right);
        }

        private SyntaxList<MemberDeclarationSyntax>? Members(
            string container,
            SyntaxList<MemberDeclarationSyntax> old,
            SyntaxList<MemberDeclarationSyntax> @new,
            SyntaxList<MemberDeclarationSyntax> disk)
        {
            var oldKeys = Keys(old);
            var newKeys = Keys(@new);
            var result = disk.ToList();
            var resultKeys = Keys(disk);
            int before = problems.Count;

            for (int index = 0; index < old.Count; index++)
            {
                if (newKeys.Contains(oldKeys[index]) || resultKeys.IndexOf(oldKeys[index]) is not (var at and >= 0))
                {
                    continue;
                }

                if (Same(result[at], old[index]))
                {
                    result.RemoveAt(at);
                    resultKeys.RemoveAt(at);
                    changes.Add($"removed {Name(container, old[index])}");
                }
                else
                {
                    problems.Add($"{path}: {Name(container, old[index])} was changed by hand, and these fields remove it. Remove it by hand.");
                }
            }

            for (int index = 0; index < @new.Count; index++)
            {
                int earlier = oldKeys.IndexOf(newKeys[index]);

                if (earlier < 0 || (Same(old[earlier], @new[index]) && old[earlier] is not BaseNamespaceDeclarationSyntax))
                {
                    continue;
                }

                int at = resultKeys.IndexOf(newKeys[index]);

                if (at < 0)
                {
                    problems.Add(
                        $"{path}: {Name(container, @new[index])}, which new-feature wrote, is not in the file, and these fields change it. Put it "
                        + $"back, or make the change by hand:\n{Indented(@new[index])}");

                    continue;
                }

                if (Member(container, old[earlier], @new[index], result[at]) is { } merged)
                {
                    result[at] = merged;
                }
            }

            for (int index = 0; index < @new.Count; index++)
            {
                if (oldKeys.Contains(newKeys[index]))
                {
                    continue;
                }

                if (resultKeys.Contains(newKeys[index]))
                {
                    problems.Add(
                        $"{path}: {Name(container, @new[index])} is declared already, and these fields declare it: rename that member first.");

                    continue;
                }

                int previous = Enumerable.Range(0, index).Reverse().Select(candidate => resultKeys.IndexOf(newKeys[candidate])).FirstOrDefault(found => found >= 0, -1);
                int next = Enumerable.Range(index + 1, @new.Count - index - 1).Select(candidate => resultKeys.IndexOf(newKeys[candidate])).FirstOrDefault(found => found >= 0, -1);
                int position = previous >= 0 ? previous + 1 : next >= 0 ? next : result.Count;

                if (position == 0 && result.Count > 0 && newKeys.IndexOf(resultKeys[0]) is var shifted and >= 0
                    && result[0].GetLeadingTrivia().All(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
                {
                    result[0] = result[0].WithLeadingTrivia(@new[shifted].GetLeadingTrivia());
                }

                result.Insert(position, @new[index]);
                resultKeys.Insert(position, newKeys[index]);
                changes.Add($"added {Name(container, @new[index])}");
            }

            return problems.Count > before ? null : SyntaxFactory.List(result);
        }

        private MemberDeclarationSyntax? Member(string container, MemberDeclarationSyntax old, MemberDeclarationSyntax @new, MemberDeclarationSyntax disk)
        {
            switch (old, @new, disk)
            {
                case (BaseNamespaceDeclarationSyntax oldSpace, BaseNamespaceDeclarationSyntax newSpace, BaseNamespaceDeclarationSyntax diskSpace):
                    return Members(container, oldSpace.Members, newSpace.Members, diskSpace.Members) is { } inside ? diskSpace.WithMembers(inside) : null;

                case (TypeDeclarationSyntax oldType, TypeDeclarationSyntax newType, TypeDeclarationSyntax diskType) when oldType.Kind() == diskType.Kind():
                    return Type(container, oldType, newType, diskType);

                default:
                    if (Same(disk, old))
                    {
                        changes.Add($"rewrote {Name(container, @new)}");

                        return @new;
                    }

                    problems.Add(
                        $"{path}: {Name(container, old)} differs from what new-feature wrote, so new-field leaves it to you. Make this change "
                        + $"in it by hand:\n{Difference(old, @new)}");

                    return null;
            }
        }

        private TypeDeclarationSyntax? Type(string container, TypeDeclarationSyntax old, TypeDeclarationSyntax @new, TypeDeclarationSyntax disk)
        {
            string name = Qualified(container, old.Identifier.Text);

            if (Members(name, old.Members, @new.Members, disk.Members) is not { } members)
            {
                return null;
            }

            if (Same(Header(old), Header(@new)))
            {
                return disk.WithMembers(members);
            }

            if (Same(Header(disk), Header(old)))
            {
                changes.Add($"rewrote the declaration of {name}");

                return @new.WithMembers(members);
            }

            problems.Add(
                $"{path}: the declaration of {name} differs from what new-feature wrote, so new-field leaves it to you. Make this change "
                + $"in it by hand:\n{Difference(Header(old), Header(@new))}");

            return null;
        }

        private static TypeDeclarationSyntax Header(TypeDeclarationSyntax type) => type.WithMembers(default);

        private static List<string> Keys(SyntaxList<MemberDeclarationSyntax> members)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            return [.. members.Select(member =>
            {
                string key = Key(member);
                int count = seen.TryGetValue(key, out int earlier) ? earlier + 1 : 0;

                seen[key] = count;

                return count == 0 ? key : $"{key}#{count}";
            })];
        }

        private static string Key(MemberDeclarationSyntax member) => member switch
        {
            BaseNamespaceDeclarationSyntax space => $"namespace {space.Name}",
            BaseTypeDeclarationSyntax type => $"{type.Kind()} {type.Identifier.Text}",
            DelegateDeclarationSyntax @delegate => $"delegate {@delegate.Identifier.Text}",
            MethodDeclarationSyntax method => $"method {method.ExplicitInterfaceSpecifier}{method.Identifier.Text}`{method.Arity}",
            ConstructorDeclarationSyntax constructor => constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? "static constructor" : "constructor",
            PropertyDeclarationSyntax property => $"property {property.ExplicitInterfaceSpecifier}{property.Identifier.Text}",
            EventDeclarationSyntax @event => $"event {@event.Identifier.Text}",
            BaseFieldDeclarationSyntax field => $"field {string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.Text))}",
            OperatorDeclarationSyntax @operator => $"operator {@operator.OperatorToken.Text}",
            ConversionOperatorDeclarationSyntax conversion => $"conversion {conversion.Type}",
            _ => member.Kind().ToString(),
        };

        private static string Name(string container, MemberDeclarationSyntax member) => member switch
        {
            BaseTypeDeclarationSyntax type => Qualified(container, type.Identifier.Text),
            MethodDeclarationSyntax method => $"{Qualified(container, method.Identifier.Text)}({string.Join(", ", method.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString()))})",
            ConstructorDeclarationSyntax => $"the constructor of {container}",
            PropertyDeclarationSyntax property => Qualified(container, property.Identifier.Text),
            BaseFieldDeclarationSyntax field => Qualified(container, string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.Text))),
            _ => $"{Key(member)} of {container}",
        };

        private static string Qualified(string container, string name) => container.Length == 0 ? name : $"{container}.{name}";

        private static string Indented(SyntaxNode node) =>
            string.Join('\n', node.ToFullString().Trim('\n', '\r').Split('\n').Select(line => "    " + line.TrimEnd('\r')));

        private static string Difference(SyntaxNode old, SyntaxNode @new)
        {
            string[] left = old.ToFullString().Trim('\n', '\r').Split('\n');
            string[] right = @new.ToFullString().Trim('\n', '\r').Split('\n');
            int[,] common = new int[left.Length + 1, right.Length + 1];

            for (int i = left.Length - 1; i >= 0; i--)
            {
                for (int j = right.Length - 1; j >= 0; j--)
                {
                    common[i, j] = left[i] == right[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
                }
            }

            var lines = new List<string>();
            int l = 0, r = 0;

            while (l < left.Length || r < right.Length)
            {
                if (l < left.Length && r < right.Length && left[l] == right[r])
                {
                    l++;
                    r++;
                }
                else if (r < right.Length && (l == left.Length || common[l, r + 1] >= common[l + 1, r]))
                {
                    lines.Add("  + " + right[r++].TrimEnd('\r'));
                }
                else
                {
                    lines.Add("  - " + left[l++].TrimEnd('\r'));
                }
            }

            return string.Join('\n', lines);
        }
    }
}
