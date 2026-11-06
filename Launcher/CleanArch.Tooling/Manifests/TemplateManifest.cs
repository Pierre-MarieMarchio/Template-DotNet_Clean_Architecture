using System.Text.Json;

namespace CleanArch.Tooling.Manifests;

public enum OptionKind
{
    Choice,
    Bool,
    Text,
    WholeNumber,
}

public sealed record OptionChoice(string Value, string Description);

public sealed record TemplateOption(
    string Symbol,
    string CliName,
    string Label,
    string Description,
    OptionKind Kind,
    IReadOnlyList<OptionChoice> Choices,
    string? DefaultValue,
    Condition? RequiredWhen,
    bool AllowsMany = false)
{
    public static IReadOnlyList<string> Values(string? answer) =>
        answer?.Split(Condition.ValueSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

    public string Joined(IEnumerable<string> values)
    {
        var chosen = values.ToHashSet(StringComparer.Ordinal);
        return string.Join(Condition.ValueSeparator, Choices.Select(choice => choice.Value).Where(chosen.Contains));
    }
}

public sealed record Refusal(string Symbol, string CliName, Condition When, string Explanation);

public sealed class TemplateManifest
{
    private const string _refusedChoice = "refused";
    private const string _neverSetPrefix = "Never set. ";

    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly IReadOnlyDictionary<string, string> _parameterDefaults;

    private TemplateManifest(
        TemplateIdentity identity,
        string? version,
        IReadOnlyList<TemplateOption> options,
        IReadOnlyList<TemplateOption> parameters,
        IReadOnlyList<Refusal> refusals,
        IReadOnlyDictionary<string, Condition> computed,
        IReadOnlyDictionary<string, string> parameterDefaults)
    {
        Identity = identity.Identity;
        ShortName = identity.ShortName;
        DefaultName = identity.DefaultName;
        PostActionIds = identity.PostActionIds;
        DeclaresGuids = identity.DeclaresGuids;
        Version = version;
        Options = options;
        Parameters = parameters;
        Refusals = refusals;
        Computed = computed;
        _parameterDefaults = parameterDefaults;
    }

    public string? Identity { get; }

    public string ShortName { get; }

    public IReadOnlyList<string> PostActionIds { get; }

    public bool DeclaresGuids { get; }

    public string? DefaultName { get; }

    public string? Version { get; }

    public IReadOnlyList<TemplateOption> Options { get; }

    public IReadOnlyList<TemplateOption> Parameters { get; }

    public IReadOnlyList<Refusal> Refusals { get; }

    public IReadOnlyDictionary<string, Condition> Computed { get; }

    public static TemplateManifest Read(string templateJson, string? commandLineHostJson, string? ideHostJson)
    {
        using var template = JsonDocument.Parse(WithoutBom(templateJson), _jsonOptions);
        using var commandLineHost = commandLineHostJson is null ? null : JsonDocument.Parse(WithoutBom(commandLineHostJson), _jsonOptions);
        using var ideHost = ideHostJson is null ? null : JsonDocument.Parse(WithoutBom(ideHostJson), _jsonOptions);

        JsonElement root = template.RootElement;
        JsonElement symbols = root.GetProperty("symbols");
        var read = new SymbolReader(commandLineHost, ideHost);
        foreach (JsonProperty symbol in symbols.EnumerateObject())
        {
            read.Add(symbol);
        }

        return new TemplateManifest(
            IdentityOf(root),
            symbols.TryGetProperty("templateVersion", out JsonElement version)
                && version.TryGetProperty("parameters", out JsonElement parameters)
                ? Text(parameters, "value")
                : null,
            [.. VisibleOrder(ideHost, read.Declared).Select(name => read.Options[name])],
            [.. read.Declared.Select(name => read.Options[name])],
            read.Refusals,
            read.Computed,
            read.ParameterDefaults);
    }

    public string? Resolve(string symbol, IReadOnlyDictionary<string, string> answers)
    {
        if (answers.TryGetValue(symbol, out string? answer))
        {
            return answer;
        }

        if (Computed.TryGetValue(symbol, out Condition? condition))
        {
            return condition.IsTrue(name => Resolve(name, answers)) ? Condition.True : Condition.False;
        }

        return _parameterDefaults.GetValueOrDefault(symbol);
    }

    public bool Holds(Condition condition, IReadOnlyDictionary<string, string> answers) =>
        condition.IsTrue(name => Resolve(name, answers));

    public IReadOnlyList<Refusal> RefusalsFor(IReadOnlyDictionary<string, string> answers) =>
        [.. Refusals.Where(refusal => (answers.ContainsKey(ItemName) || !refusal.When.Symbols.Contains(ItemName)) && Holds(refusal.When, answers))];

    public const string ItemName = "name";

    public IReadOnlySet<string> ParametersBehind(Condition condition)
    {
        var parameters = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(condition.Symbols);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (pending.TryPop(out string? symbol))
        {
            if (!seen.Add(symbol))
            {
                continue;
            }

            if (Computed.TryGetValue(symbol, out Condition? inner))
            {
                foreach (string name in inner.Symbols)
                {
                    pending.Push(name);
                }
            }
            else
            {
                parameters.Add(symbol);
            }
        }

        return parameters;
    }

    public IReadOnlyList<string> CommandArguments(IReadOnlyDictionary<string, string> answers)
    {
        var arguments = new List<string>();

        foreach (TemplateOption option in Options)
        {
            if (!answers.TryGetValue(option.Symbol, out string? value))
            {
                continue;
            }

            bool required = option.RequiredWhen is { } requiredWhen && Holds(requiredWhen, answers);
            if (required || !string.Equals(value, option.DefaultValue, StringComparison.Ordinal))
            {
                foreach (string one in option.AllowsMany ? TemplateOption.Values(value) : [value])
                {
                    arguments.Add("--" + option.CliName);
                    arguments.Add(one);
                }
            }
        }

        return arguments;
    }

    private static TemplateIdentity IdentityOf(JsonElement root)
    {
        string shortName = root.TryGetProperty("shortName", out JsonElement names) && names.ValueKind == JsonValueKind.Array && names.GetArrayLength() > 0
            ? names[0].GetString()!
            : Text(root, "shortName") ?? throw new FormatException("The template has no shortName.");
        string[] postActionIds = root.TryGetProperty("postActions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array
            ? [.. actions.EnumerateArray().Where(action => action.ValueKind == JsonValueKind.Object).Select(action => Text(action, "actionId") ?? string.Empty)]
            : [];
        bool declaresGuids = root.TryGetProperty("guids", out JsonElement guids) && guids.ValueKind == JsonValueKind.Array && guids.GetArrayLength() > 0;

        return new TemplateIdentity(Text(root, "identity"), shortName, Text(root, "defaultName"), postActionIds, declaresGuids);
    }

    private static string WithoutBom(string json) => json.TrimStart('﻿');

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<string> VisibleOrder(JsonDocument? ideHost, List<string> declared)
    {
        if (ideHost is null || !ideHost.RootElement.TryGetProperty("symbolInfo", out JsonElement info))
        {
            return declared;
        }

        return info.EnumerateArray()
            .Where(entry => entry.TryGetProperty("isVisible", out JsonElement visible) && visible.ValueKind == JsonValueKind.True)
            .Select(entry => Text(entry, "id")!)
            .Where(declared.Contains);
    }

    private sealed record TemplateIdentity(string? Identity, string ShortName, string? DefaultName, IReadOnlyList<string> PostActionIds, bool DeclaresGuids);

    private sealed class SymbolReader(JsonDocument? commandLineHost, JsonDocument? ideHost)
    {
        public Dictionary<string, Condition> Computed { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ParameterDefaults { get; } = new(StringComparer.Ordinal);

        public List<Refusal> Refusals { get; } = [];

        public Dictionary<string, TemplateOption> Options { get; } = new(StringComparer.Ordinal);

        public List<string> Declared { get; } = [];

        public void Add(JsonProperty symbol)
        {
            string type = Text(symbol.Value, "type") ?? string.Empty;
            if (type == "computed" && Text(symbol.Value, "value") is { } expression)
            {
                Computed[symbol.Name] = Condition.Parse(expression);
            }
            else if (type == "parameter" && IsRefusal(symbol.Value))
            {
                Refusals.Add(ReadRefusal(symbol, CliName(commandLineHost, symbol.Name)));
            }
            else if (type == "parameter")
            {
                AddOption(symbol, CliName(commandLineHost, symbol.Name));
            }
        }

        private static Refusal ReadRefusal(JsonProperty symbol, string cliName)
        {
            Condition when = RequiredWhen(symbol.Value)
                ?? throw new FormatException($"The refusal '{symbol.Name}' has no isRequired condition.");
            string explanation = Text(symbol.Value, "description") ?? symbol.Name;
            if (explanation.StartsWith(_neverSetPrefix, StringComparison.Ordinal))
            {
                explanation = explanation[_neverSetPrefix.Length..];
            }

            return new Refusal(symbol.Name, cliName, when, explanation);
        }

        private void AddOption(JsonProperty symbol, string cliName)
        {
            string? defaultValue = Text(symbol.Value, "defaultValue");
            if (defaultValue is not null)
            {
                ParameterDefaults[symbol.Name] = defaultValue;
            }

            Declared.Add(symbol.Name);
            Options[symbol.Name] = new TemplateOption(
                symbol.Name,
                cliName,
                Label(ideHost, symbol.Name) ?? Text(symbol.Value, "displayName") ?? symbol.Name,
                Text(symbol.Value, "description") ?? string.Empty,
                Kind(symbol.Value),
                Choices(symbol.Value),
                defaultValue,
                RequiredWhen(symbol.Value),
                symbol.Value.TryGetProperty("allowMultipleValues", out JsonElement many) && many.ValueKind == JsonValueKind.True);
        }

        private static bool IsRefusal(JsonElement symbol) =>
            symbol.TryGetProperty("choices", out JsonElement choices)
            && choices.GetArrayLength() > 0
            && choices.EnumerateArray().All(choice => Text(choice, "choice") == _refusedChoice);

        private static Condition? RequiredWhen(JsonElement symbol)
        {
            if (!symbol.TryGetProperty("isRequired", out JsonElement required))
            {
                return null;
            }

            return required.ValueKind switch
            {
                JsonValueKind.True => Condition.Parse(Condition.True),
                JsonValueKind.False => null,
                JsonValueKind.String => Condition.Parse(required.GetString()!),
                _ => throw new FormatException("An isRequired is neither a boolean nor a condition."),
            };
        }

        private static OptionKind Kind(JsonElement symbol) => Text(symbol, "datatype") switch
        {
            "choice" => OptionKind.Choice,
            "bool" => OptionKind.Bool,
            "integer" or "int" => OptionKind.WholeNumber,
            _ => OptionKind.Text,
        };

        private static List<OptionChoice> Choices(JsonElement symbol) =>
            symbol.TryGetProperty("choices", out JsonElement choices)
                ? [.. choices.EnumerateArray().Select(choice => new OptionChoice(Text(choice, "choice")!, Text(choice, "description") ?? string.Empty))]
                : [];

        private static string CliName(JsonDocument? commandLineHost, string symbol) =>
            commandLineHost is not null
            && commandLineHost.RootElement.TryGetProperty("symbolInfo", out JsonElement info)
            && info.TryGetProperty(symbol, out JsonElement entry)
            && Text(entry, "longName") is { Length: > 0 } longName
                ? longName
                : symbol;

        private static string? Label(JsonDocument? ideHost, string symbol) =>
            ideHost is not null && ideHost.RootElement.TryGetProperty("symbolInfo", out JsonElement info)
                ? info.EnumerateArray()
                    .Where(entry => Text(entry, "id") == symbol && entry.TryGetProperty("name", out _))
                    .Select(entry => Text(entry.GetProperty("name"), "text"))
                    .FirstOrDefault()
                : null;
    }
}
