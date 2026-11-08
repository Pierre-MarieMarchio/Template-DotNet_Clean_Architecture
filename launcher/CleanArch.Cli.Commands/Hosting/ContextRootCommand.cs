using System.CommandLine;

namespace CleanArch.Cli.Commands.Hosting;

internal sealed class ContextRootCommand(CommandContext context, string description) : RootCommand(description)
{
    public CommandContext Context => context;
}
