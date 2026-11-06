using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace CleanArch.Cli;

public static class CleanArchCommand
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const string Name = "dotnet cleanarch";

    private const string _description = "Describe this project, or run one of its commands.";

    private static readonly IReadOnlyList<CommandRegistration> _registrations =
    [
        HelpCommand.Registration,
        FormatCommand.Registration,
    ];

    public static int Run(IReadOnlyList<string> arguments, string workingDirectory, TextWriter output, TextWriter error) =>
        Run(arguments, workingDirectory, output, error, _registrations);

    internal static int Run(IReadOnlyList<string> arguments, string workingDirectory, TextWriter output, TextWriter error, IReadOnlyList<CommandRegistration> registrations)
    {
        RootCommand root = CreateRoot(CommandContext.Locate(workingDirectory), registrations);
        HelpOption help = root.Options.OfType<HelpOption>().Single();
        help.Action = new UsageNamedAfterTheDotnetCommand((HelpAction)help.Action!);

        return root.Parse([.. arguments]).Invoke(new InvocationConfiguration { Output = output, Error = error });
    }

    private static RootCommand CreateRoot(CommandContext context, IReadOnlyList<CommandRegistration> registrations)
    {
        var root = new RootCommand(_description);
        root.SetAction(parseResult => Describe(context, parseResult));

        foreach (CommandRegistration registration in registrations)
        {
            Command command = registration.Build(context);
            string? refusal = registration.Refusal(context);
            if (refusal is not null)
            {
                command.Hidden = true;
                command.SetAction(parseResult => Refuse(parseResult, refusal));
            }

            root.Add(command);
        }

        return root;
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
