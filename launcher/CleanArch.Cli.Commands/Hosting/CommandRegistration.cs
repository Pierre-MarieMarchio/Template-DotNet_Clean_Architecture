using System.CommandLine;

namespace CleanArch.Cli.Commands.Hosting;

internal sealed record CommandRegistration(Func<CommandContext, string?> Refusal, Func<CommandContext, Command> Build);
