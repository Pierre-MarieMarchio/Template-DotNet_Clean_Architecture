using System.Text;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddUseCase;

internal static class UseCaseInputs
{
    internal static readonly string[] _recordMembers =
        ["Deconstruct", "EqualityContract", "Equals", "GetHashCode", "GetType", "MemberwiseClone", "PrintMembers", "ToString"];

    internal static readonly string[] _predefinedTypes =
        ["bool", "byte", "char", "decimal", "double", "float", "int", "long", "object", "sbyte", "short", "string", "uint", "ulong", "ushort"];

    internal static (bool HasId, List<Parameter> Inputs) ReadInputs(string input, List<string> problems)
    {
        var inputs = new List<Parameter>();
        bool hasId = false;

        if (input == "none")
        {
            return (false, inputs);
        }

        string[] entries = [.. TopLevelEntries(input).Select(entry => entry.Trim())];

        for (int index = 0; index < entries.Length; index++)
        {
            hasId |= ReadEntry(input, entries[index], index, inputs, problems);
        }

        foreach (string twice in inputs.GroupBy(parameter => parameter.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"--input names {twice} more than once; an input is named once.");
        }

        return (hasId, inputs);
    }

    private static bool ReadEntry(string input, string entry, int index, List<Parameter> inputs, List<string> problems)
    {
        int colon = entry.IndexOf(':', StringComparison.Ordinal);

        if (entry == "id")
        {
            if (index > 0)
            {
                problems.Add("id comes first in --input, once: it is the aggregate's id the route carries, e.g. --input \"id, Note:string\".");
            }

            return true;
        }

        if (entry.Length == 0)
        {
            problems.Add($"--input '{input}' holds an empty entry, a comma with nothing on one side of it.");
        }
        else if (entry == "none")
        {
            problems.Add("none stands alone in --input: it is a message without input.");
        }
        else if (colon < 0)
        {
            problems.Add($"'{entry}' in --input is neither id nor an input written Name:Type, e.g. MaxCount:int.");
        }
        else
        {
            ReadParameter(entry[..colon].Trim(), entry[(colon + 1)..].Trim(), inputs, problems);
        }

        return false;
    }

    private static void ReadParameter(string name, string type, List<Parameter> inputs, List<string> problems)
    {
        var (canonical, problem) = TypeSyntax.Read(type);
        int refusedBefore = problems.Count;

        if (!CSharpNames.PascalCase.IsMatch(name))
        {
            problems.Add($"'{name}' in --input is not an input name: it is PascalCase, e.g. MaxCount.");
        }
        else if (_recordMembers.Contains(name, StringComparer.Ordinal))
        {
            problems.Add($"'{name}' in --input is a member every record declares already. Name the input otherwise.");
        }

        if (problem is not null)
        {
            problems.Add($"'{type}' in --input, the type of {name}, is not a C# type: {problem}.");
        }

        if (problems.Count == refusedBefore)
        {
            inputs.Add(new Parameter(name, canonical!, $"{UseCasePlan.Sentence(name)} is required."));
        }
    }

    internal static IEnumerable<string> TopLevelEntries(string input)
    {
        int depth = 0;
        int start = 0;

        for (int index = 0; index < input.Length; index++)
        {
            switch (input[index])
            {
                case '<' or '(' or '[':
                    depth++;
                    break;
                case '>' or ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return input[start..index];
                    start = index + 1;
                    break;
            }
        }

        yield return input[start..];
    }

    internal sealed class TypeSyntax
    {
        private static readonly Regex _token = new(@"\s*(?:([A-Za-z_][A-Za-z0-9_]*)|(::)|([<>,?\[\]().])|(\S))", RegexOptions.None, TimeSpan.FromSeconds(1));

        private readonly List<string> _tokens;

        private int _at;

        private TypeSyntax(List<string> tokens) => _tokens = tokens;

        private string? Peek => _at < _tokens.Count ? _tokens[_at] : null;

        internal static (string? Type, string? Problem) Read(string text)
        {
            var tokens = new List<string>();

            foreach (Match match in _token.Matches(text))
            {
                if (match.Groups[4].Success)
                {
                    return (null, $"'{match.Groups[4].Value}' has no place in a type name");
                }

                tokens.Add(match.Value.Trim());
            }

            var reader = new TypeSyntax(tokens);

            try
            {
                string type = reader.Type();

                return reader.Peek is { } extra
                    ? (null, $"'{extra}' follows a type that was already complete")
                    : (type, null);
            }
            catch (FormatException exception)
            {
                return (null, exception.Message);
            }
        }

        private string Next() =>
            _at < _tokens.Count ? _tokens[_at++] : throw new FormatException("it stops short of a complete type");

        private void Expect(string token)
        {
            if (Peek != token)
            {
                throw new FormatException(Peek is null ? $"it stops where '{token}' is due" : $"'{Peek}' stands where '{token}' is due");
            }

            _at++;
        }

        private string Identifier()
        {
            string token = Next();

            if (!char.IsAsciiLetter(token[0]) && token[0] != '_')
            {
                throw new FormatException($"'{token}' stands where a name is due");
            }

            return token;
        }

        private string Type()
        {
            var type = new StringBuilder(Peek == "(" ? Tuple() : Named());

            while (Peek is "?" or "[")
            {
                if (Next() == "?")
                {
                    type.Append(type[^1] == '?' ? throw new FormatException("a type is made nullable once") : '?');

                    continue;
                }

                int rank = 0;

                while (Peek == ",")
                {
                    _at++;
                    rank++;
                }

                Expect("]");
                type.Append('[').Append(',', rank).Append(']');
            }

            return type.ToString();
        }

        private string Tuple()
        {
            Expect("(");
            var elements = new List<string> { Element() };

            while (Peek == ",")
            {
                _at++;
                elements.Add(Element());
            }

            Expect(")");

            return elements.Count >= 2
                ? "(" + string.Join(", ", elements) + ")"
                : throw new FormatException("a tuple holds two elements or more");
        }

        private string Element()
        {
            string type = Type();

            if (Peek is not { } name || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            {
                return type;
            }

            return CSharpNames.Keywords.Contains(name, StringComparer.Ordinal)
                ? throw new FormatException($"'{name}' is a C# keyword, not an element name")
                : type + " " + Next();
        }

        private string Named()
        {
            string global = string.Empty;

            if (Peek == "global" && _at + 1 < _tokens.Count && _tokens[_at + 1] == "::")
            {
                _at += 2;
                global = "global::";
            }

            string first = Identifier();

            if (CSharpNames.Keywords.Contains(first, StringComparer.Ordinal))
            {
                return _predefinedTypes.Contains(first, StringComparer.Ordinal) && global.Length == 0 && Peek is not ("." or "<")
                    ? first
                    : throw new FormatException($"'{first}' is a C# keyword, not a type");
            }

            var named = new StringBuilder(global + first + TypeArguments());

            while (Peek == ".")
            {
                _at++;
                string segment = Identifier();

                named.Append('.').Append(CSharpNames.Keywords.Contains(segment, StringComparer.Ordinal) ? throw new FormatException($"'{segment}' is a C# keyword, not a type") : segment).Append(TypeArguments());
            }

            return named.ToString();
        }

        private string TypeArguments()
        {
            if (Peek != "<")
            {
                return string.Empty;
            }

            _at++;
            var arguments = new List<string> { Type() };

            while (Peek == ",")
            {
                _at++;
                arguments.Add(Type());
            }

            Expect(">");

            return "<" + string.Join(", ", arguments) + ">";
        }
    }
}
