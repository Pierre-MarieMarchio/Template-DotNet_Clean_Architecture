using System.CommandLine;
using System.Reflection;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli;

internal static class GeneratorCommand
{
    public static string Version { get; } =
        typeof(GeneratorCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(GeneratorCommand).Assembly.GetName().Version!.ToString();

    public static Option<bool> DryRun(string description) => new("--dry-run") { Description = description };

    public static Option<string?> Text(string name, string description) => new(name) { Description = description };

    public static Option<string> RequiredText(string name, string description) => new(name) { Description = description, Required = true };

    public static void SetGeneration(
        Command command,
        LocatedProject? project,
        Func<ParseResult, bool> dryRun,
        Func<ParseResult, IExecutor> executor,
        Func<ParseResult, GenerationContext, int> generate)
    {
        if (project is null)
        {
            return;
        }

        command.SetAction(parseResult => generate(
            parseResult,
            new GenerationContext(
                project,
                dryRun(parseResult),
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                executor(parseResult))));
    }

    public static void RefuseWithoutAuthentication(CommandContext context, Command command)
    {
        if (context.Project?.File.Options.Auth == "none")
        {
            CleanArchCommand.Refuse(
                command,
                $"This project has no add {command.Name} command: its {ProjectLocator.FileName} records auth none, so it authenticates nobody, "
                + "and every feature add feature writes (--access owner or shared), and so every feature add field extends, needs an "
                + "authenticated caller. Add an authentication scheme and an ICurrentUser that reads it before scaffolding a feature, "
                + "or write a public feature by hand.");
        }
    }

    public static IExecutor Executor(ParseResult parseResult) =>
        new ProjectExecutor(
            CommandContext.Of(parseResult).Processes,
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error);

    public static int ShowHelp(ParseResult parseResult)
    {
        parseResult.CommandResult.Command.Parse(["--help"]).Invoke(parseResult.InvocationConfiguration);
        return CleanArchCommand.Succeeded;
    }
}

internal sealed class ProjectExecutor(IProcessLauncher processes, TextWriter output, TextWriter error) : IExecutor
{
    private static readonly string[] _windowsShims = ["pnpm"];

    public int Run(TextEdits.Command command) =>
        command.FileName == Tool.Name
            ? BuiltInCommands.Run(command.Arguments, command.WorkingDirectory, output, error, processes)
            : processes.Launch(CallOf(OnThisSystem(command)), output, error);

    public (int Code, string Output) Read(TextEdits.Command command)
    {
        using var read = new StringWriter();
        int code = processes.Launch(CallOf(command), read, TextWriter.Null);
        return (code, read.ToString());
    }

    private static TextEdits.Command OnThisSystem(TextEdits.Command command) =>
        OperatingSystem.IsWindows() && _windowsShims.Contains(command.FileName, StringComparer.Ordinal) ? command with { FileName = command.FileName + ".cmd" } : command;

    private static ExternalCall CallOf(TextEdits.Command command) => new(command.FileName, command.Arguments, command.WorkingDirectory);
}
