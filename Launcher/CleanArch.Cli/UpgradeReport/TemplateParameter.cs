namespace CleanArch.Cli.UpgradeReport;

internal sealed record TemplateParameter(string Name, string CliName, string? Default, string[] Choices);
