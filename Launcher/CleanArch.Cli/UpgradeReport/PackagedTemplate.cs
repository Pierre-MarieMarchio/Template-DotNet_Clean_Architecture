using System.Text.Json.Nodes;

namespace CleanArch.Cli.UpgradeReport;

internal sealed record PackagedTemplate(string Identity, string ShortName, JsonObject Symbols, JsonArray PostActions, IReadOnlyDictionary<string, string> LongNames)
{
    public const string Main = "CleanArch.WebApi.CSharp";

    public const string WebAppItem = "CleanArch.WebApi.WebApp.TypeScript";

    public const string WebPackageItem = "CleanArch.WebApi.WebPackage.TypeScript";

    public const string PresentationItem = "CleanArch.WebApi.Presentation.CSharp";

    public IEnumerable<TemplateParameter> Parameters() =>
        Symbols
            .Where(symbol => symbol.Value?["type"]?.GetValue<string>() == "parameter")
            .Select(symbol => new TemplateParameter(
                symbol.Key,
                symbol.Value!["datatype"]?.GetValue<string>() ?? "text",
                symbol.Value["defaultValue"]?.ToString(),
                [.. (symbol.Value["choices"] as JsonArray ?? []).Select(choice => choice?["choice"]?.GetValue<string>() ?? string.Empty)]))
            .Where(parameter => parameter.Choices is not ["refused"]);

    public Dictionary<string, TemplateParameter> ParametersByName() =>
        Parameters().ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);

    public bool Declares(string symbol) => Parameters().Any(parameter => parameter.Name == symbol);

    public string Option(string symbol) => "--" + LongNames.GetValueOrDefault(symbol, symbol);
}
