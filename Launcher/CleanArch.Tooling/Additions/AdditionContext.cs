using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;
using Spectre.Console;

namespace CleanArch.Tooling.Additions;

public sealed record AdditionArguments(
    string? Name,
    IReadOnlyDictionary<string, string> TemplateOptions,
    string? TemplateSource,
    string? Hive);

public sealed record AdditionContext(
    Questionnaire Questionnaire,
    TemplateSource Source,
    TemplateEngine Engine,
    IProcessRunner Processes,
    IAnsiConsole Console,
    AdditionArguments Arguments,
    string? Version,
    string Invocation,
    bool DryRun = false);
