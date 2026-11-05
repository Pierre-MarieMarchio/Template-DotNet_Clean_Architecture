using System.CommandLine;

namespace CleanArch.Cli;

internal sealed record CommandRegistration(Func<ProjectFile?, string?> Refusal, Func<CommandContext, Command> Build);
