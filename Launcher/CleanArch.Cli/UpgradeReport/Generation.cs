namespace CleanArch.Cli.UpgradeReport;

internal sealed record Generation(string Tree, TemplatePackage Content, IReadOnlyList<string> Notes);
