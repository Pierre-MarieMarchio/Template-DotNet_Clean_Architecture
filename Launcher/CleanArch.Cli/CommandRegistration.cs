using System.CommandLine;

namespace CleanArch.Cli;

internal sealed record CommandRegistration(Func<CommandContext, string?> Refusal, Func<CommandContext, Command> Build);
