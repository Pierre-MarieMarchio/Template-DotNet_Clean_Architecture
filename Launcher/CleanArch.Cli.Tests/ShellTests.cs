using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class ShellTests
{
    [Theory]
    [InlineData(true, "git status\nclean\n")]
    [InlineData(false, "")]
    public void ACapturedRun_KeepsTheOutputAndShowsItOnlyWhenAsked(bool echo, string shown)
    {
        var output = new StringWriter { NewLine = "\n" };
        var shell = new Shell(new FakeProcesses((_, callOutput, _) =>
        {
            callOutput.WriteLine("clean");
            return 7;
        }), output, new StringWriter());

        CapturedRun run = shell.Capture(new ExternalCall("git", ["status"], "."), echo);

        run.ExitCode.ShouldBe(7);
        run.Output.ShouldBe("clean\n".Replace("\n", Environment.NewLine));
        output.ToString().ShouldBe(shown);
    }
}
