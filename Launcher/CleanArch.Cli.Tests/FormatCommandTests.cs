using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class FormatCommandTests : IDisposable
{
    private static readonly byte[] _unnormalised = [0xEF, 0xBB, 0xBF, .. "# Hi\r\n"u8.ToArray()];

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<string> _calls = [];
    private readonly SampleProject _project = new(SampleProject.Minimal);
    private Func<string, TextWriter, int> _dotnet = (_, _) => CleanArchCommand.Succeeded;

    public FormatCommandTests() =>
        ExternalCommand.Handler = (call, output, _) =>
        {
            string line = string.Join(' ', call.Arguments);
            _calls.Add(line);
            return _dotnet(line, output);
        };

    [Fact]
    public void Format_NormalisesMarkdownOutsideTheSkippedFolders_AndLeavesCSharpToDotnetFormat()
    {
        string readme = Write("README.md", _unnormalised);
        string template = Write(".github/PULL_REQUEST_TEMPLATE.md", _unnormalised);
        string built = Write("Src/obj/notes.md", _unnormalised);
        string hook = Write(".git/hook.md", _unnormalised);
        byte[] csharp = "namespace Acme;\r\n"u8.ToArray();
        string source = Write("Src/Program.cs", csharp);

        Run("format").ShouldBe(CleanArchCommand.Succeeded);

        File.ReadAllBytes(readme).ShouldBe("# Hi\n"u8.ToArray());
        File.ReadAllBytes(template).ShouldBe("# Hi\n"u8.ToArray());
        File.ReadAllBytes(built).ShouldBe(_unnormalised);
        File.ReadAllBytes(hook).ShouldBe(_unnormalised);
        File.ReadAllBytes(source).ShouldBe(csharp);
        _output.ToString().ShouldContain(".github/PULL_REQUEST_TEMPLATE.md: saved as UTF-8 without a BOM, with LF endings.");
        _output.ToString().ShouldContain("README.md: saved as UTF-8 without a BOM, with LF endings.");
    }

    [Fact]
    public void Format_RunsDotnetFormat_ThenNormalisesMarkdown_ThenChecksTheNames()
    {
        string readme = Write("README.md", _unnormalised);
        var markdownWhenCalled = new List<byte[]>();
        _dotnet = (_, _) =>
        {
            markdownWhenCalled.Add(File.ReadAllBytes(readme));
            return CleanArchCommand.Succeeded;
        };

        Run("format").ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(2);
        _calls[0].ShouldNotContain("--verify-no-changes");
        _calls[1].ShouldContain("IDE1006");
        markdownWhenCalled[0].ShouldBe(_unnormalised);
        markdownWhenCalled[1].ShouldBe("# Hi\n"u8.ToArray());
    }

    [Fact]
    public void Format_StopsWhereDotnetFormatFails()
    {
        string readme = Write("README.md", _unnormalised);
        _dotnet = (_, _) => 3;

        Run("format").ShouldBe(3);

        _calls.Count.ShouldBe(1);
        File.ReadAllBytes(readme).ShouldBe(_unnormalised);
    }

    [Fact]
    public void Format_NamesEachNamingViolationRelativeToTheRoot_AndFails()
    {
        string file = Path.Combine(_project.Root, "Src", "Acme", "Widget.cs");
        string violation = $"{file}(12,17): warning IDE1006: Naming rule violation: Missing prefix: '_' [{_project.Root}/Src/Acme/Acme.csproj]";
        string other = $"{file}(3,9): error IDE1006: Naming rule violation: These words must begin with upper case characters: widget [{_project.Root}/Src/Acme/Acme.csproj]";
        _dotnet = (arguments, output) =>
        {
            if (!arguments.Contains("IDE1006", StringComparison.Ordinal))
            {
                return CleanArchCommand.Succeeded;
            }

            output.WriteLine(violation);
            output.WriteLine(other);
            output.WriteLine(violation);
            return 2;
        };

        Run("format").ShouldBe(CleanArchCommand.Failed);

        _error.ToString().Split(Environment.NewLine).Where(line => line.StartsWith("  ", StringComparison.Ordinal)).ShouldBe(
        [
            "  Src/Acme/Widget.cs:12: Naming rule violation: Missing prefix: '_'",
            "  Src/Acme/Widget.cs:3: Naming rule violation: These words must begin with upper case characters: widget",
        ]);
        _error.ToString().ShouldNotContain(_project.Root);
    }

    [Fact]
    public void FormatCheck_VerifiesWithoutWriting_AndFailsWhereMarkdownWouldChange()
    {
        string readme = Write("docs/README.md", _unnormalised);
        Write("CONTRIBUTING.md", "# Fine\n"u8.ToArray());

        Run("format", "--check").ShouldBe(CleanArchCommand.Failed);

        _calls.ShouldHaveSingleItem().ShouldContain("--verify-no-changes");
        File.ReadAllBytes(readme).ShouldBe(_unnormalised);
        _error.ToString().ShouldContain("docs/README.md: would be saved as UTF-8 without a BOM, with LF endings.");
        _error.ToString().ShouldNotContain("CONTRIBUTING.md");
    }

    [Fact]
    public void FormatCheck_StopsWhereDotnetFormatFindsAChange()
    {
        Write("README.md", _unnormalised);
        _dotnet = (_, _) => 2;

        Run("format", "--check").ShouldBe(2);

        _error.ToString().ShouldNotContain("README.md");
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _project.Dispose();
        _output.Dispose();
        _error.Dispose();
    }

    private string Write(string relative, byte[] content)
    {
        string path = Path.Combine(_project.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private int Run(params string[] arguments) =>
        CleanArchCommand.Run(arguments, _project.Root, _output, _error);
}
