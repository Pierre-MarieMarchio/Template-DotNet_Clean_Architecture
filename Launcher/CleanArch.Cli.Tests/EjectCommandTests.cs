using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class EjectCommandTests : IDisposable
{
    private const string _implicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public static TheoryData<string> EjectableCommands =>
        [.. EjectCommand.Ejectable().Select(CommandSource.TypeOf)];

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    [Fact]
    public void Eject_WritesTheCommandAndWhatItNeeds_AsAFileBasedAppInTools()
    {
        using var project = new SampleProject(SampleProject.Minimal);

        Run(["eject", "format"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        byte[] written = File.ReadAllBytes(Path.Combine(project.Root, "Tools", "format.cs"));
        written.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        string app = Encoding.UTF8.GetString(written, 3, written.Length - 3);
        app.ShouldStartWith(
            "#:property UseAppHost=false\n#:property PublishAot=false\n#:property AssemblyName=dotnet-cleanarch-format\n"
            + "#:property ManagePackageVersionsCentrally=false\n#:package System.CommandLine@2.0.14\n");
        app.ShouldNotContain("\r");
        app.ShouldContain("internal static class FormatCommand");
        app.ShouldNotContain("internal static class BuildCommand");
    }

    [Fact]
    public void EjectDryRun_SaysWhatItWouldWrite_AndWritesNothing()
    {
        using var project = new SampleProject(SampleProject.Minimal);

        Run(["eject", "build", "--dry-run"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        File.Exists(Path.Combine(project.Root, "Tools", "build.cs")).ShouldBeFalse();
        _output.ToString().ShouldStartWith("Would write Tools/build.cs, ");
        _output.ToString().ShouldContain("BuildCommand.cs");
    }

    [Fact]
    public void Eject_RefusesACommandThatIsThisProjectsOwnAlready()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        project.Folder("Tools");
        File.WriteAllText(Path.Combine(project.Root, "Tools", "build.cs"), "return 3;\n");

        Run(["eject", "build"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("Tools/build.cs exists");
        File.ReadAllText(Path.Combine(project.Root, "Tools", "build.cs")).ShouldBe("return 3;\n");
    }

    [Fact]
    public void Eject_RefusesACommandThatDoesNotApplyToTheProject()
    {
        using var project = new SampleProject(SampleProject.Minimal);

        Run(["eject", "run"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("run does not apply to this project");
        _output.GetStringBuilder().Clear();
        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        string description = JsonNode.Parse(_output.ToString())!["commands"]!.AsArray().OfType<JsonObject>()
            .Single(command => command["name"]!.GetValue<string>() == "eject")["arguments"]![0]!["description"]!.GetValue<string>();
        description.ShouldContain("format");
        description.ShouldNotContain("run");
        File.Exists(Path.Combine(project.Root, "Tools", "run.cs")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("help")]
    [InlineData("adopt")]
    [InlineData("add")]
    [InlineData("remove")]
    public void Eject_TakesOnlyACommandTheProjectCanOwn(string command)
    {
        using var project = new SampleProject(SampleProject.Minimal);

        Run(["eject", command], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldNotBeEmpty();
        Directory.Exists(Path.Combine(project.Root, "Tools")).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(EjectableCommands))]
    public void EveryEjectableCommand_CompilesAlone_FromTheSourceTheToolShips(string type)
    {
        string app = CommandSource.FileBasedApp("command", type, EjectCommand.CommandLineVersion);

        Compile(app, out _).Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void TheToolShipsTheSourceOfEveryFileItIsBuiltFrom_ButItsEntryAndItsList()
    {
        string[] sources = [.. Directory.EnumerateFiles(Path.Combine(Repository.Root, "Launcher", "CleanArch.Cli"), "*.cs")
            .Select(Path.GetFileName).OfType<string>()
            .Except(["Program.cs", "BuiltInCommands.cs"])
            .Order(StringComparer.Ordinal)];

        CommandSource.Shipped.Keys.Order(StringComparer.Ordinal).ShouldBe(sources);
        CommandSource.Shipped["FormatCommand.cs"].ShouldBe(
            File.ReadAllText(Path.Combine(Repository.Root, "Launcher", "CleanArch.Cli", "FormatCommand.cs")).TrimStart('﻿'));
    }

    [Fact]
    public void AnEjectedCommand_DoesWhatTheBuiltInOneDoes()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        File.WriteAllText(Path.Combine(project.Folder("Src"), "Sample.cs"), "using Zeta;\r\nusing System;\r\n");
        Run(["format", "--check"], project.Root).ShouldBe(CleanArchCommand.Failed);
        string builtIn = _error.ToString();

        int exitCode = RunEjected(CommandSource.FileBasedApp("format", nameof(FormatCommand), EjectCommand.CommandLineVersion), ["--check"], project.Root, out string ejectedError);

        exitCode.ShouldBe(CleanArchCommand.Failed);
        ejectedError.ShouldBe(builtIn);
    }

    private static System.Collections.Immutable.ImmutableArray<Diagnostic> Compile(string app, out CSharpCompilation compilation)
    {
        CSharpParseOptions options = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFeatures([new("FileBasedProgram", "true")]);
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var written = CSharpCompilation.Create(
            "ejected",
            [CSharpSyntaxTree.ParseText(app, options), CSharpSyntaxTree.ParseText(_implicitUsings, options)],
            [.. platform
                .Append(typeof(System.CommandLine.Command).Assembly.Location)
                .Except([typeof(CleanArchCommand).Assembly.Location, typeof(EjectCommandTests).Assembly.Location])
                .Select(path => MetadataReference.CreateFromFile(path))],
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create([.. new AnalyzerFileReference(RegexGenerator, new GeneratorLoader()).GetGenerators(LanguageNames.CSharp)], parseOptions: options)
            .RunGeneratorsAndUpdateCompilation(written, out Compilation generated, out _);
        compilation = (CSharpCompilation)generated;
        return compilation.GetDiagnostics();
    }

    private static string RegexGenerator
    {
        get
        {
            string dotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
            string referencePack = Directory.EnumerateDirectories(Path.Combine(dotnet, "packs", "Microsoft.NETCore.App.Ref"))
                .MaxBy(version => Version.Parse(Path.GetFileName(version)))!;
            return Path.Combine(referencePack, "analyzers", "dotnet", "cs", "System.Text.RegularExpressions.Generator.dll");
        }
    }

    private sealed class GeneratorLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }

    private static int RunEjected(string app, string[] arguments, string workingDirectory, out string error)
    {
        Compile(app, out CSharpCompilation compilation).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        using var image = new MemoryStream();
        compilation.Emit(image).Success.ShouldBeTrue();
        image.Position = 0;

        var context = new AssemblyLoadContext("ejected", isCollectible: true);
        string previousDirectory = Environment.CurrentDirectory;
        TextWriter previousOutput = Console.Out;
        TextWriter previousError = Console.Error;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        try
        {
            Environment.CurrentDirectory = workingDirectory;
            Console.SetOut(output);
            Console.SetError(errors);
            MethodInfo entry = context.LoadFromStream(image).EntryPoint!;
            int exitCode = (int)entry.Invoke(null, [arguments])!;
            error = errors.ToString();
            return exitCode;
        }
        finally
        {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
            Environment.CurrentDirectory = previousDirectory;
            context.Unload();
        }
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);
}
