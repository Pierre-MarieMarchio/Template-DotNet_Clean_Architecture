namespace CleanArch.Cli.UpgradeReport;

internal sealed record TemplateParameter(string Name, string Datatype, string? Default, string[] Choices);
