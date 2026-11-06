using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Runtime.CompilerServices;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Hosting;

public static class CleanArchCommand
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const string Name = "dotnet cleanarch";

    private static readonly ConditionalWeakTable<Command, string> _refusals = [];

    private const string _description = "Describe this project, or run one of its commands.";

    internal static int Run(IReadOnlyList<string> arguments, string workingDirectory, TextWriter output, TextWriter error, IReadOnlyList<CommandRegistration> registrations, IProcessLauncher processes)
    {
        CommandContext context = CommandContext.Locate(workingDirectory, processes);
        if (arguments.Count > 0 && context.LocalCommands.FirstOrDefault(local => local.Name == arguments[0]) is { } named)
        {
            return named.Run(context, arguments.Skip(1), output, error);
        }

        return Invoke(arguments, context, output, error, registrations);
    }

    internal static int RunBuiltIn(IReadOnlyList<string> arguments, string workingDirectory, TextWriter output, TextWriter error, IReadOnlyList<CommandRegistration> registrations, IProcessLauncher processes) =>
        Invoke(arguments, CommandContext.Locate(workingDirectory, processes) with { LocalCommands = [] }, output, error, registrations);

    private static int Invoke(IReadOnlyList<string> arguments, CommandContext context, TextWriter output, TextWriter error, IReadOnlyList<CommandRegistration> registrations)
    {
        RootCommand root = CreateRoot(context, registrations);
        HelpOption help = root.Options.OfType<HelpOption>().Single();
        help.Action = new UsageNamedAfterTheDotnetCommand((HelpAction)help.Action!);

        return root.Parse([.. arguments]).Invoke(new InvocationConfiguration { Output = output, Error = error });
    }

    private static RootCommand CreateRoot(CommandContext context, IReadOnlyList<CommandRegistration> registrations)
    {
        var root = new RootCommand(_description);
        root.SetAction(parseResult => Describe(context, parseResult));

        HashSet<string> builtIns = [];
        foreach (CommandRegistration registration in registrations)
        {
            Command command = registration.Build(context);
            builtIns.Add(command.Name);
            if (context.LocalCommands.Any(local => local.Name == command.Name))
            {
                continue;
            }

            string? refusal = registration.Refusal(context);
            if (refusal is not null)
            {
                Refuse(command, refusal);
            }

            root.Add(command);
        }

        foreach (LocalCommand local in context.LocalCommands)
        {
            root.Add(new LocalCommandLine(local, context, builtIns.Contains(local.Name)));
        }

        return root;
    }

    internal static void Refuse(Command command, string refusal)
    {
        command.Hidden = true;
        command.TreatUnmatchedTokensAsErrors = false;
        foreach (Option option in command.Options)
        {
            option.Required = false;
        }

        foreach (Argument argument in command.Arguments)
        {
            argument.Arity = new ArgumentArity(0, argument.Arity.MaximumNumberOfValues);
        }

        _refusals.AddOrUpdate(command, refusal);
        command.SetAction(parseResult => Refuse(parseResult, refusal));
        foreach (Command subcommand in command.Subcommands)
        {
            Refuse(subcommand, refusal);
        }
    }

    private static int Describe(CommandContext context, ParseResult parseResult)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        if (context.RefusedProjectFile is { } refused)
        {
            error.WriteLine(refused.Message);
            return Failed;
        }

        if (context.Project is not { } project)
        {
            error.WriteLine(context.OutsideProject);
            return Failed;
        }

        output.WriteLine($"{project.File.Name}, generated from {project.File.Template.Package} {project.File.Template.Version}, at {project.Root}");
        return Succeeded;
    }

    internal static string? RefusalOf(Command command) => _refusals.TryGetValue(command, out string? refusal) ? refusal : null;

    private static int Refuse(ParseResult parseResult, string refusal)
    {
        parseResult.InvocationConfiguration.Error.WriteLine(refusal);
        return Failed;
    }
}

internal sealed class UsageNamedAfterTheDotnetCommand(HelpAction help) : SynchronousCommandLineAction
{
    public override int Invoke(ParseResult parseResult)
    {
        if (CleanArchCommand.RefusalOf(parseResult.CommandResult.Command) is { } refusal)
        {
            parseResult.InvocationConfiguration.Error.WriteLine(refusal);
            return CleanArchCommand.Failed;
        }

        TextWriter output = parseResult.InvocationConfiguration.Output;
        using var written = new StringWriter();
        parseResult.InvocationConfiguration.Output = written;
        try
        {
            int exitCode = help.Invoke(parseResult);
            output.Write(written.ToString().Replace($"  {RootCommand.ExecutableName} ", $"  {CleanArchCommand.Name} ", StringComparison.Ordinal));
            return exitCode;
        }
        finally
        {
            parseResult.InvocationConfiguration.Output = output;
        }
    }
}
