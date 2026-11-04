using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class TemplateMarkers
{
    private static readonly Regex _directive = new(@"^\s*(?://|/\*|<!--)?#(?<kind>if|elseif|elif|else|endif)\b(?<condition>.*)$", RegexOptions.None, TimeSpan.FromSeconds(5));

    internal static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    internal static Dictionary<int, Condition> Conditions(string[] lines)
    {
        var conditions = new Dictionary<int, Condition>();

        for (int index = 0; index < lines.Length; index++)
        {
            if (_directive.Match(lines[index]) is { Success: true } directive && directive.Groups["kind"].Value is "if" or "elseif" or "elif")
            {
                string condition = directive.Groups["condition"].Value.Trim();
                conditions[index] = Condition.Parse(condition.EndsWith("*/", StringComparison.Ordinal) ? condition[..^2] : condition.EndsWith("-->", StringComparison.Ordinal) ? condition[..^3].TrimEnd() : condition);
            }
        }

        return conditions;
    }

    internal static IEnumerable<string> Kept(string[] lines, Dictionary<int, Condition> conditions, Func<string, object> symbol)
    {
        var branches = new Stack<(bool Active, bool Taken)>();

        for (int index = 0; index < lines.Length; index++)
        {
            if (_directive.Match(lines[index]) is { Success: true } directive)
            {
                Branch(branches, directive.Groups["kind"].Value, conditions.TryGetValue(index, out var condition) ? () => (bool)condition.Evaluate(symbol) : () => false);
            }
            else if (branches.All(branch => branch.Active))
            {
                yield return lines[index];
            }
        }
    }

    private static void Branch(Stack<(bool Active, bool Taken)> branches, string kind, Func<bool> holds)
    {
        switch (kind)
        {
            case "if":
                bool active = holds();
                branches.Push((active, active));
                break;
            case "elseif" or "elif":
                var (_, taken) = branches.Pop();
                bool chosen = !taken && holds();
                branches.Push((chosen, taken || chosen));
                break;
            case "else":
                var (_, before) = branches.Pop();
                branches.Push((!before, true));
                break;
            default:
                branches.Pop();
                break;
        }
    }
}

internal sealed record Form(Func<string, object> Value, string Options);

internal sealed class TemplateForms
{
    private const string _refused = "refusedOptions";

    private readonly Dictionary<string, Condition> _computed;

    private readonly List<(string Name, object[] Domain)> _parameters;

    private readonly List<Source> _sources;

    private readonly Lazy<(string[] Symbols, List<(ulong Vector, long Combination)> Vectors)> _forms;

    private readonly Dictionary<ulong, List<(ulong Vector, long Combination)>> _byMask = new();

    private TemplateForms(Dictionary<string, Condition> computed, List<(string Name, object[] Domain)> parameters, List<Source> sources, IReadOnlyCollection<string> fileSymbols)
    {
        _computed = computed;
        _parameters = parameters;
        _sources = sources;
        _forms = new(() => Enumerate(fileSymbols));
    }

    internal static TemplateForms Load(string root, IReadOnlyCollection<string> fileSymbols)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".template.config", "template.json")));
        var computed = new Dictionary<string, Condition>(StringComparer.Ordinal);
        var parameters = new List<(string Name, object[] Domain)>();

        foreach (var symbol in manifest.RootElement.GetProperty("symbols").EnumerateObject())
        {
            string type = symbol.Value.GetProperty("type").GetString() ?? "";

            if (type == "computed")
            {
                computed[symbol.Name] = Condition.Parse(symbol.Value.GetProperty("value").GetString() ?? "");
            }
            else if (type == "parameter" && Domain(symbol.Value) is { Length: > 1 } domain)
            {
                parameters.Add((symbol.Name, domain));
            }
        }

        parameters.ShouldNotBeEmpty("template.json declares no option, so the audit enumerates no form.");

        return new TemplateForms(computed, parameters, [.. manifest.RootElement.GetProperty("sources").EnumerateArray().Select(Source.Read)], fileSymbols);
    }

    internal IEnumerable<Form> FormsOf(string path, IEnumerable<string> fileSymbols)
    {
        var (symbols, _) = _forms.Value;
        var generating = _sources.Where(source => source.Holds(path)).ToList();
        var read = fileSymbols.Concat(generating.SelectMany(source => source.NamesFor(path))).Where(name => name != _refused).ToHashSet(StringComparer.Ordinal);

        read.Except(symbols, StringComparer.Ordinal).ShouldBeEmpty($"{path} reads symbols the audit does not enumerate.");

        ulong mask = symbols.Select((name, bit) => read.Contains(name) ? 1UL << bit : 0UL).Aggregate(0UL, (all, bit) => all | bit);

        foreach (var (vector, combination) in Representatives(mask))
        {
            object Value(string name) => name != _refused && ((vector >> Array.IndexOf(symbols, name)) & 1UL) == 1UL;

            if (generating.Any(source => source.Generates(path, Value)))
            {
                yield return new Form(Value, Options(combination));
            }
        }
    }

    private List<(ulong Vector, long Combination)> Representatives(ulong mask)
    {
        if (!_byMask.TryGetValue(mask, out var representatives))
        {
            var seen = new HashSet<ulong>();
            representatives = [.. _forms.Value.Vectors.Where(form => seen.Add(form.Vector & mask))];
            _byMask[mask] = representatives;
        }

        return representatives;
    }

    private (string[] Symbols, List<(ulong Vector, long Combination)> Vectors) Enumerate(IReadOnlyCollection<string> fileSymbols)
    {
        string[] symbols = [.. fileSymbols.Concat(_sources.SelectMany(source => source.AllNames)).Where(name => name != _refused).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        symbols.Length.ShouldBeLessThanOrEqualTo(64, "The audit packs a form's symbols into 64 bits.");

        var seen = new HashSet<ulong>();
        var vectors = new List<(ulong Vector, long Combination)>();
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        long count = _parameters.Aggregate(1L, (product, parameter) => product * parameter.Domain.Length);

        object Symbol(string name)
        {
            if (!values.TryGetValue(name, out object? value))
            {
                value = _computed.TryGetValue(name, out var condition) ? condition.Evaluate(Symbol) : false;
                values[name] = value;
            }

            return value;
        }

        for (long combination = 0; combination < count; combination++)
        {
            Assign(values, combination);

            if ((bool)Symbol(_refused))
            {
                continue;
            }

            ulong vector = 0;

            for (int bit = 0; bit < symbols.Length; bit++)
            {
                vector |= Symbol(symbols[bit]) is true ? 1UL << bit : 0UL;
            }

            if (seen.Add(vector))
            {
                vectors.Add((vector, combination));
            }
        }

        return (symbols, vectors);
    }

    private void Assign(Dictionary<string, object> values, long combination)
    {
        values.Clear();

        foreach (var (name, domain) in _parameters)
        {
            values[name] = domain[combination % domain.Length];
            combination /= domain.Length;
        }
    }

    private string Options(long combination)
    {
        var options = new List<string>();

        foreach (var (name, domain) in _parameters)
        {
            long index = combination % domain.Length;
            combination /= domain.Length;

            if (index > 0)
            {
                options.Add(domain[index] switch
                {
                    string[] values => string.Join(' ', values.Select(value => $"--{Kebab(name)} {value}")),
                    bool flag => $"--{Kebab(name)} {(flag ? "true" : "false")}",
                    var value => $"--{Kebab(name)} {value}",
                });
            }
        }

        return options.Count == 0 ? "the defaults" : string.Join(' ', options);
    }

    private static string Kebab(string name) =>
        Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "-", RegexOptions.None, TimeSpan.FromSeconds(1)).ToLowerInvariant();

    private static object[] Domain(JsonElement parameter)
    {
        string datatype = parameter.GetProperty("datatype").GetString() ?? "";
        string fallback = parameter.TryGetProperty("defaultValue", out var given) ? given.GetString() ?? "" : "";

        if (datatype == "bool")
        {
            bool byDefault = fallback == "true";
            return [byDefault, !byDefault];
        }

        if (datatype != "choice")
        {
            return [];
        }

        string[] choices = [.. parameter.GetProperty("choices").EnumerateArray().Select(choice => choice.GetProperty("choice").GetString() ?? "")];
        string[] ordered = [.. choices.Where(choice => choice == fallback), .. choices.Where(choice => choice != fallback)];

        return parameter.TryGetProperty("allowMultipleValues", out var multiple) && multiple.GetBoolean()
            ? [.. Subsets(ordered)]
            : ordered;
    }

    private static IEnumerable<object> Subsets(string[] choices) =>
        Enumerable.Range(1, (1 << choices.Length) - 1)
            .Select(bits => choices.Where((_, index) => ((bits >> index) & 1) == 1).ToArray());
}

internal sealed record Source(string Prefix, Condition? When, Regex[] Include, Regex[] Exclude, (Condition? When, Regex[] Exclude)[] Modifiers)
{
    internal IEnumerable<string> AllNames =>
        (When?.Names ?? Enumerable.Empty<string>()).Concat(Modifiers.SelectMany(modifier => modifier.When?.Names ?? Enumerable.Empty<string>()));

    internal static Source Read(JsonElement source)
    {
        string folder = source.TryGetProperty("source", out var given) ? given.GetString() ?? "./" : "./";

        return new Source(
            folder.StartsWith("./", StringComparison.Ordinal) ? folder[2..] : folder,
            ConditionOf(source),
            Globs(source, "include"),
            Globs(source, "exclude"),
            [.. Elements(source, "modifiers").Select(modifier => (ConditionOf(modifier), Globs(modifier, "exclude")))]);
    }

    internal bool Holds(string path) =>
        path.StartsWith(Prefix, StringComparison.Ordinal)
        && (Include.Length == 0 || Include.Any(glob => glob.IsMatch(path[Prefix.Length..])))
        && !Exclude.Any(glob => glob.IsMatch(path[Prefix.Length..]));

    internal IEnumerable<string> NamesFor(string path) =>
        (When?.Names ?? Enumerable.Empty<string>()).Concat(Modifiers
            .Where(modifier => modifier.Exclude.Any(glob => glob.IsMatch(path[Prefix.Length..])))
            .SelectMany(modifier => modifier.When?.Names ?? Enumerable.Empty<string>()));

    internal bool Generates(string path, Func<string, object> symbol) =>
        (When is null || (bool)When.Evaluate(symbol))
        && !Modifiers.Any(modifier => modifier.Exclude.Any(glob => glob.IsMatch(path[Prefix.Length..]))
            && (modifier.When is null || (bool)modifier.When.Evaluate(symbol)));

    private static IEnumerable<JsonElement> Elements(JsonElement element, string key) =>
        element.TryGetProperty(key, out var array) ? [.. array.EnumerateArray()] : [];

    private static Condition? ConditionOf(JsonElement element) =>
        element.TryGetProperty("condition", out var condition) ? Condition.Parse(condition.GetString() ?? "") : null;

    private static Regex[] Globs(JsonElement element, string key) =>
        [.. Elements(element, key).Select(glob => Glob(glob.GetString() ?? ""))];

    private static Regex Glob(string pattern)
    {
        string body = Regex.Escape(pattern.StartsWith("./", StringComparison.Ordinal) ? pattern[2..] : pattern)
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal);

        return new Regex($"^{body}$", RegexOptions.None, TimeSpan.FromSeconds(1));
    }
}

internal sealed class Condition
{
    private static readonly Regex _token = new(@"\G\s*(?:""(?<text>[^""]*)""|(?<operator>&&|\|\||==|!=|<=|>=|<|>|!|\(|\))|(?<name>[A-Za-z_]\w*))\s*", RegexOptions.None, TimeSpan.FromSeconds(1));

    private readonly Func<Func<string, object>, object> _evaluate;

    private Condition(Func<Func<string, object>, object> evaluate, IReadOnlySet<string> names)
    {
        _evaluate = evaluate;
        Names = names;
    }

    internal IReadOnlySet<string> Names { get; }

    internal object Evaluate(Func<string, object> symbol) => _evaluate(symbol);

    internal static Condition Parse(string text)
    {
        var tokens = new List<(string Kind, string Value)>();
        string condition = text.Trim();

        for (int position = 0; position < condition.Length;)
        {
            var token = _token.Match(condition, position);

            if (!token.Success || token.Length == 0)
            {
                throw new FormatException($"'{text}' is not a template condition.");
            }

            tokens.Add(token.Groups["text"].Success ? ("text", token.Groups["text"].Value)
                : token.Groups["operator"].Success ? ("operator", token.Groups["operator"].Value)
                : ("name", token.Groups["name"].Value));
            position += token.Length;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var parser = new Parser(tokens, names);
        var evaluate = parser.Or();

        return parser.AtEnd ? new Condition(evaluate, names) : throw new FormatException($"'{text}' is not a template condition.");
    }

    private sealed class Parser(List<(string Kind, string Value)> tokens, HashSet<string> names)
    {
        private int _next;

        internal bool AtEnd => _next == tokens.Count;

        internal Func<Func<string, object>, object> Or()
        {
            var left = And();

            while (Takes("||"))
            {
                var (first, second) = (left, And());
                left = symbol => (bool)first(symbol) || (bool)second(symbol);
            }

            return left;
        }

        private Func<Func<string, object>, object> And()
        {
            var left = Comparison();

            while (Takes("&&"))
            {
                var (first, second) = (left, Comparison());
                left = symbol => (bool)first(symbol) && (bool)second(symbol);
            }

            return left;
        }

        private Func<Func<string, object>, object> Comparison()
        {
            var left = Unary();
            bool equal = Takes("==");

            if (equal || Takes("!="))
            {
                var right = Unary();
                return symbol => Same(left(symbol), right(symbol)) == equal;
            }

            foreach (string ordering in (string[])["<=", ">=", "<", ">"])
            {
                if (Takes(ordering))
                {
                    var right = Unary();
                    return symbol => Orders(ordering, left(symbol), right(symbol));
                }
            }

            return left;
        }

        private Func<Func<string, object>, object> Unary()
        {
            if (Takes("!"))
            {
                var operand = Unary();
                return symbol => !(bool)operand(symbol);
            }

            if (Takes("("))
            {
                var inner = Or();
                return Takes(")") ? inner : throw new FormatException("A parenthesis of a template condition is not closed.");
            }

            if (AtEnd || tokens[_next].Kind == "operator")
            {
                throw new FormatException("A template condition lacks an operand.");
            }

            var (kind, value) = tokens[_next++];

            if (kind == "text" || value is "true" or "false")
            {
                object constant = kind == "text" ? value : value == "true";
                return _ => constant;
            }

            names.Add(value);
            return symbol => symbol(value);
        }

        private bool Takes(string symbol)
        {
            if (!AtEnd && tokens[_next] is ("operator", var value) && value == symbol)
            {
                _next++;
                return true;
            }

            return false;
        }

        private static bool Orders(string ordering, object left, object right) =>
            left is string first && right is string second && string.Compare(first, second, StringComparison.OrdinalIgnoreCase) switch
            {
                < 0 => ordering is "<" or "<=",
                0 => ordering is "<=" or ">=",
                > 0 => ordering is ">" or ">=",
            };

        private static bool Same(object left, object right) => (left, right) switch
        {
            (string[] values, string value) => values.Contains(value, StringComparer.Ordinal),
            (string value, string[] values) => values.Contains(value, StringComparer.Ordinal),
            (bool flag, string value) => string.Equals(flag ? "true" : "false", value, StringComparison.Ordinal),
            _ => Equals(left, right),
        };
    }
}
