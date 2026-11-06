using CleanArch.Create.Setup;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;
using Spectre.Console;

namespace CleanArch.Create.Commands;

public sealed record Machine(IAnsiConsole Console, string WorkingDirectory, IProcessRunner Processes, Interruption Interruption);

public sealed record Session(
    Machine Machine,
    Arguments Parsed,
    TemplateSource Source,
    Questionnaire Questionnaire,
    TemplateEngine Dotnet,
    string? Version,
    ProjectFacts? Project)
{
    public Conversation Conversation => Questionnaire.Conversation;
}
