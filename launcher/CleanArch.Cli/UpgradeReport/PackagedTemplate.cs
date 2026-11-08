using CleanArch.Tooling.Manifests;

namespace CleanArch.Cli.UpgradeReport;

internal sealed record PackagedTemplate(string Identity, TemplateManifest Manifest)
{
    public const string Main = "CleanArch.WebApi.CSharp";

    public const string WebAppItem = "CleanArch.WebApi.WebApp.TypeScript";

    public const string WebPackageItem = "CleanArch.WebApi.WebPackage.TypeScript";

    public const string PresentationItem = "CleanArch.WebApi.Presentation.CSharp";

    public string ShortName => Manifest.ShortName;

    public IEnumerable<TemplateParameter> Parameters() =>
        Manifest.Parameters.Select(option => new TemplateParameter(option.Symbol, option.CliName, option.DefaultValue, [.. option.Choices.Select(choice => choice.Value)]));

    public Dictionary<string, TemplateParameter> ParametersByName() =>
        Parameters().ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);

    public bool Declares(string symbol) => Manifest.Parameters.Any(option => option.Symbol == symbol);

    public string Option(string symbol) => "--" + (Manifest.Parameters.FirstOrDefault(option => option.Symbol == symbol)?.CliName ?? symbol);
}
