using System.Collections.ObjectModel;
using CleanArch.Tooling.Manifests;

namespace CleanArch.Create.Commands;

public static class LauncherUsage
{
    private const int _nameWidth = 15;

    public static string Text() =>
        $"""
        Usage: {LauncherCommands.Invocation} [-- [{string.Join('|', LauncherCommands.Names)}] [options] [--<template option> <value>]...]

        dnx takes -y, --yes, --help and --version for itself: put the launcher's own arguments after --.

        Commands
        {string.Join('\n', LauncherCommands.All.SelectMany(Summary))}

        Options
          -n, --name <name>            the project, app, package or presentation name
          -o, --output <folder>        where to create the project (default: ./<name>)
          -y, --yes                    use the default for every option you don't pass
          --setup                      after new, run every setup step without asking: commit, bootstrap, commit, copy .env
          --template-source <path>     use a checkout of the template or a .nupkg instead of the published package
          --hive <folder>              keep the template engine's settings in this folder (dotnet new --debug:custom-hive)
          -h, --help                   show this help

        Without a terminal (in CI or a pipe), pass every answer as an option, or add --yes.
        """;

    public static string TemplateOptions(TemplateManifest manifest, string subject, IReadOnlyDictionary<string, Condition> askedWhen)
    {
        IEnumerable<string> lines = manifest.Options.Select(option =>
        {
            string values = option.Kind switch
            {
                OptionKind.Choice => string.Join('|', Offer.For(manifest, option, ReadOnlyDictionary<string, string>.Empty, askedWhen).Values),
                OptionKind.Bool => "true|false",
                _ => "<value>",
            };
            string flag = $"  --{option.CliName} {values}";
            string fallback = option.DefaultValue is null ? string.Empty : $" (default: {option.DefaultValue})";
            string repeated = option.AllowsMany ? $", one --{option.CliName} per value" : string.Empty;
            return $"{flag,-46} {option.Label}{repeated}{fallback}";
        });

        return string.Join(Environment.NewLine, lines.Prepend($"{char.ToUpperInvariant(subject[0])}{subject[1..]} options, asked one by one when you don't pass them ({manifest.ShortName})"));
    }

    private static IEnumerable<string> Summary(ILauncherCommand command) =>
        command.Summary.Select((line, index) => $"  {(index == 0 ? command.Name : string.Empty).PadRight(_nameWidth)}{line}");
}
