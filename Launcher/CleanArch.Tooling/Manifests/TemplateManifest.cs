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
    Condition? RequiredWhen);

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
        string shortName,
        string? defaultName,
        string? version,
        IReadOnlyList<TemplateOption> options,
        IReadOnlyList<Refusal> refusals,
        IReadOnlyDictionary<string, Condition> computed,
        IReadOnlyDictionary<string, string> parameterDefaults)
    {
        ShortName = shortName;
        DefaultName = defaultName;
        Version = version;
        Options = options;
        Refusals = refusals;
        Computed = computed;
        _parameterDefaults = parameterDefaults;
    }

    public string ShortName { get; }

    public string? DefaultName { get; }

    public string? Version { get; }

    public IReadOnlyList<TemplateOption> Options { get; }

    public IReadOnlyList<Refusal> Refusals { get; }

    public IReadOnlyDictionary<string, Condition> Computed { get; }

    public static TemplateManifest Read(string templateJson, string? commandLineHostJson, string? ideHostJson)
    {
        using var template = JsonDocument.Parse(WithoutBom(templateJson), _jsonOptions);
        using var commandLineHost = commandLineHostJson is null ? null : JsonDocument.Parse(WithoutBom(commandLineHostJson), _jsonOptions);
        using var ideHost = ideHostJson is null ? null : JsonDocument.Parse(WithoutBom(ideHostJson), _jsonOptions);

        JsonElement root = template.RootElement;
        JsonElement symbols = root.GetProperty("symbols");

        var computed = new Dictionary<string, Condition>(StringComparer.Ordinal);
        var parameterDefaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var refusals = new List<Refusal>();
        var options = new Dictionary<string, TemplateOption>(StringComparer.Ordinal);
        var declared = new List<string>();

        foreach (JsonProperty symbol in symbols.EnumerateObject())
        {
            string type = Text(symbol.Value, "type") ?? string.Empty;

            if (type == "computed" && Text(symbol.Value, "value") is { } expression)
            {
                computed[symbol.Name] = Condition.Parse(expression);
                continue;
            }

            if (type != "parameter")
            {
                continue;
            }

            string cliName = CliName(commandLineHost, symbol.Name);
            if (IsRefusal(symbol.Value))
            {
                Condition when = RequiredWhen(symbol.Value)
                    ?? throw new FormatException($"The refusal '{symbol.Name}' has no isRequired condition.");
                string explanation = Text(symbol.Value, "description") ?? symbol.Name;
                if (explanation.StartsWith(_neverSetPrefix, StringComparison.Ordinal))
                {
                    explanation = explanation[_neverSetPrefix.Length..];
                }

                refusals.Add(new Refusal(symbol.Name, cliName, when, explanation));
                continue;
            }

            string? defaultValue = Text(symbol.Value, "defaultValue");
            if (defaultValue is not null)
            {
                parameterDefaults[symbol.Name] = defaultValue;
            }

            declared.Add(symbol.Name);
            options[symbol.Name] = new TemplateOption(
                symbol.Name,
                cliName,
                Label(ideHost, symbol.Name) ?? Text(symbol.Value, "displayName") ?? symbol.Name,
                Text(symbol.Value, "description") ?? string.Empty,
                Kind(symbol.Value),
                Choices(symbol.Value),
                defaultValue,
                RequiredWhen(symbol.Value));
        }

        return new TemplateManifest(
            Text(root, "shortName") ?? throw new FormatException("The template has no shortName."),
            Text(root, "defaultName"),
            symbols.TryGetProperty("templateVersion", out JsonElement version)
                && version.TryGetProperty("parameters", out JsonElement parameters)
                ? Text(parameters, "value")
                : null,
            [.. VisibleOrder(ideHost, declared).Select(name => options[name])],
            refusals,
            computed,
            parameterDefaults);
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
        [.. Refusals.Where(refusal => Holds(refusal.When, answers))];

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
                arguments.Add("--" + option.CliName);
                arguments.Add(value);
            }
        }

        return arguments;
    }

    private static string WithoutBom(string json) => json.TrimStart('﻿');

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
}
