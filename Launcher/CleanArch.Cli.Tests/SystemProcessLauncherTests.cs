using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class SystemProcessLauncherTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("cleanarch-external-").FullName;

    [Fact]
    public void ARealProcess_StreamsItsOutputWhileItRuns_SplitsItsErrors_AndForwardsItsExitCode()
    {
        const string script = """
            echo first
            i=0
            while [ ! -f go ] && [ $i -lt 100 ]; do sleep 0.05; i=$((i + 1)); done
            if [ -f go ]; then echo second; else echo late; fi
            echo trouble >&2
            exit 3
            """;
        var output = new ReleasingWriter(Path.Combine(_folder, "go"));
        var error = new StringWriter();

        int exit = new Shell(new SystemProcessLauncher(), output, error).Run(new ExternalCall("sh", ["-c", script], _folder));

        exit.ShouldBe(3);
        output.Lines.ShouldBe(["sh -c " + "\"" + script + "\"", "first", "second"]);
        error.ToString().ShouldBe("trouble" + Environment.NewLine);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class ReleasingWriter(string release) : StringWriter
    {
        public List<string> Lines { get; } = [];

        public override void WriteLine(string? value)
        {
            Lines.Add(value ?? "");
            if (value == "first")
            {
                File.WriteAllText(release, "");
            }
        }
    }
}
