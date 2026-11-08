using System.CommandLine;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests.Architecture;

public sealed class EjectableCommandsReferenceTests
{
    [Fact]
    public void TheEjectableCommands_ReferenceOnlyTheFrameworkAndSystemCommandLine()
    {
        string commandLine = typeof(Command).Assembly.GetName().Name!;
        string framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] references = [.. typeof(CommandContext).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!)];

        references.ShouldContain(commandLine, "CleanArch.Cli.Commands does not reference System.CommandLine, so this read of its references is inert.");

        references
            .Where(name => name != commandLine && !File.Exists(Path.Combine(framework, name + ".dll")))
            .ShouldBeEmpty(
                "CleanArch.Cli.Commands references an assembly other than the shared framework and System.CommandLine. "
                + "Eject copies these commands into a file-based app that declares System.CommandLine alone, "
                + "so any other reference makes the ejected file fail to compile.");
    }
}
