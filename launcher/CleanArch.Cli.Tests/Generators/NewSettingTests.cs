using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArch.Cli.Generators.AddSetting;
using CleanArch.Cli.Generators.Shared;
using Xunit;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Tests.Generators;

public sealed class NewSettingTests
{
    [Fact]
    public void Parser() => Expectations.Hold(ParserCases);

    [Fact]
    public void Key() => Expectations.Hold(KeyCases);

    [Fact]
    public void Write() => Expectations.Hold(WriteCases);

    [Fact]
    public void DefaultsOnly() => Expectations.Hold(DefaultsOnlyCases);

    [Fact]
    public void ConfigurationParameter() => Expectations.Hold(ConfigurationParameterCases);

    [Fact]
    public void Anchor() => Expectations.Hold(AnchorCases);

    [Fact]
    public void Tree() => Expectations.Hold(TreeCases);

    [Fact]
    public void Rollback() => Expectations.Hold(RollbackCases);

    [Fact]
    public void UnrecordableEdit() => Expectations.Hold(UnrecordableEditCases);

    private const string _module = "src/Application/A.Application/ApplicationModule.cs";

    private const string _api = "src/Presentation/A.Api/appsettings.json";

    private const string _worker = "src/Presentation/A.Worker/appsettings.json";

    private const string _guide = "docs/reference/configuration.md";

    private const string _compose = "docker-compose.yml";

    private const string _environment = ".env.example";

    private const string _composition = "tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs";

    private const string _vocabulary = "tests/Architecture/A.Architecture.Tests/Rules/LayoutConventionTests.cs";

    private const string _validKeys = "MaxItems:int(1..100)=10, Interval:duration(00:00:01..01:00:00)=00:05:00, Label:string(1..20)=main, Owner:string(3..40), ApiKey:secret, Strict:bool=true, Cap:long(1..5000000000)=1000";

    private static string NewTree(string scratch)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        void Put(string relative, string content, bool bom = true)
        {
            string full = Path.Combine(root, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, [.. bom ? Encoding.UTF8.GetPreamble() : [], .. Encoding.UTF8.GetBytes(content)]);
        }

        Put("cleanarch.json", "{\n  \"name\": \"Acme\"\n}\n", bom: false);
        Directory.CreateDirectory(Path.Combine(root, "src/Application/A.Application/Features/Invoices/Services"));
        Directory.CreateDirectory(Path.Combine(root, "src/Application/A.Application/Features/Notes/Services"));
        Directory.CreateDirectory(Path.Combine(root, "tests/Application/A.Application.UnitTests"));

        Put(
            _module,
            """
            using Acme.Application.Core.Common.Events;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Application;

            public static class ApplicationModule
            {
                public static IServiceCollection AddInvoices(this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);

                    services.AddFeature("Invoices");

                    return services;
                }

                public static IServiceCollection AddNotes(this IServiceCollection services, IConfiguration configuration)
                {
                    ArgumentNullException.ThrowIfNull(services);
                    ArgumentNullException.ThrowIfNull(configuration);

                    services.AddFeature("Notes");

                    return services;
                }
            }

            """);
        Put("src/Presentation/A.Api/Program.cs", "builder.Services.AddInvoices();\nbuilder.Services.AddNotes(builder.Configuration);\n");
        Put("src/Presentation/A.Worker/Program.cs", "builder.Services.AddInvoices();\n");
        Put(_api, "{\n  \"Logging\": {\n    \"LogLevel\": {\n      \"Default\": \"Information\"\n    }\n  },\n  \"AllowedHosts\": \"*\"\n}\n");
        Put(_worker, "{\n  \"Logging\": {\n    \"LogLevel\": {\n      \"Default\": \"Information\"\n    }\n  }\n}\n");
        Put(_guide, "# Configuration\n\n## Database\n\n### `Database`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n| `MaxPoolSize` | int | `20` | 1–500. |\n\n## Notes\n\n### `NoteLimits`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n| `Max` | int | `5` | 1–9. |\n\n## Beyond the sections\n\nText.\n");
        Put(_vocabulary, "public sealed class LayoutConventionTests\n{\n    private static readonly Dictionary<string, string[]?> _vocabulary = new()\n    {\n        [\"src/Application/A.Application\"] =\n            [\n                \"Helpers\",\n                \"Model/Dtos\", \"Model/Errors\",\n                \"Ports\", \"Services/Consumers\", \"UseCases\",\n            ],\n        [\"src/Domain/A.Domain\"] =\n            [\"Model/Entities\"],\n    };\n}\n");
        Put(_compose, "name: acme\n\n# The settings every host reads.\nx-host-environment: &host-environment\n  ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}\n\n  Jwt__Key: ${JWT_KEY:?set JWT_KEY in .env}\n\nservices:\n  api:\n    environment:\n      <<: *host-environment\n", bom: false);
        Put(_environment, "# Copy to .env.\nJWT_KEY=dev\n\n# Jobs\nJOBS_ENABLED=true\n", bom: false);
        Put(_composition, "internal static class HostComposition\n{\n    internal static IReadOnlyDictionary<string, string?> ValidSettings { get; } =\n        new Dictionary<string, string?>(StringComparer.Ordinal)\n        {\n            [\"Jwt:Key\"] = JwtKey,\n\n            [\"Files:Categories:document:UploadModes:0\"] = \"Direct\",\n        };\n}\n");
        Put(".feature-manifest/Invoices.json", "{\n  \"feature\": \"Invoices\",\n  \"files\": []\n}\n", bom: false);

        return root;
    }

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args) =>
        GeneratorRun.Invoke(AddSettingCommand.Build(GeneratorRun.Project(root), _ => new FakeExecutor(_ => 0)), args);

    private static void RecordedEditsUndo(string root, Dictionary<string, string> before, Action<bool, string> expect)
    {
        var after = Snapshot(root);
        string[] edited = [.. before.Keys.Where(path => after.TryGetValue(path, out string? hash) && hash != before[path] && path != ".feature-manifest/Invoices.json").Order(StringComparer.Ordinal)];

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".feature-manifest/Invoices.json")));
        var edits = document.RootElement.TryGetProperty("edits", out var recorded) ? recorded.EnumerateArray().ToList() : [];

        expect(
            edited.Length > 0 && edited.SequenceEqual(edits.Select(edit => edit.GetProperty("path").GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            $"The manifest does not record an edit in each file the run edited, and in those alone, so remove-feature could not undo them: {string.Join(", ", edited)}");

        foreach (string path in edited)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
            bool bom = bytes is [0xEF, 0xBB, 0xBF, ..];
            List<string> lines = [.. Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)).Split('\n')];

            foreach (var edit in edits.Where(edit => edit.GetProperty("path").GetString() == path).Reverse())
            {
                string anchor = edit.GetProperty("anchor").GetString()!;
                bool preceding = edit.GetProperty("position").GetString() == "before";
                string[] added = [.. edit.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!)];
                string[] replaced = edit.TryGetProperty("replaced", out var was) ? [.. was.EnumerateArray().Select(line => line.GetString()!)] : [];
                int at = Enumerable.Range(0, lines.Count).FirstOrDefault(
                    index => lines[index] == anchor
                        && (preceding
                            ? index >= added.Length && lines.Skip(index - added.Length).Take(added.Length).SequenceEqual(added)
                            : lines.Skip(index + 1).Take(added.Length).SequenceEqual(added)),
                    -1);

                expect(at >= 0, $"The manifest records lines {(preceding ? "before" : "after")} '{anchor}' in {path} that are not there.");

                if (at >= 0)
                {
                    int start = preceding ? at - added.Length : at + 1;

                    lines.RemoveRange(start, added.Length);
                    lines.InsertRange(start, replaced);
                }
            }

            byte[] undone = [.. bom ? Encoding.UTF8.Preamble : [], .. Encoding.UTF8.GetBytes(string.Join('\n', lines))];

            expect(
                Convert.ToHexStringLower(SHA256.HashData(undone)) == before[path],
                $"Undoing the edits the manifest records in {path} does not give back the file as it was.");
        }
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "<directory>",
                StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    private static void ExpectRefusal(string root, string[] args, string fragment, Action<bool, string> expect)
    {
        var before = Snapshot(root);
        var result = Invoke(root, args);
        string label = string.Join(' ', args);

        expect(result.Code == 1, $"[{label}] exited {result.Code}, not 1.");
        expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] did not say '{fragment}': {result.Error.Split('\n')[0]}");
        expect(result.Error.Contains("Nothing was written.", StringComparison.Ordinal), $"[{label}] did not say nothing was written.");
        expect(Same(before, Snapshot(root)), $"[{label}] changed the tree.");
    }

    private static string Read(string root, string relative) =>
        File.Exists(Path.Combine(root, relative)) ? Decode(File.ReadAllBytes(Path.Combine(root, relative))) : string.Empty;

    private static void ParserCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string[] keys = ["--keys", "MaxItems:int(1..100)=10"];

        (string[] Args, string Fragment)[] refusals =
        [
            (["invoices", "Limits", .. keys], "is not a feature folder"),
            (["Invoices", "limits", .. keys], "is not a section name"),
            (["Invoices", "LimitsOptions", .. keys], "ends in 'Options'"),
            (["Invoices", "Event", .. keys], "C# keyword"),
            (["Invoices", "Logging", .. keys], "already reads"),
            (["Invoices", new string('A', 61), .. keys], "longer than 60"),
            (["Gadgets", "Limits", .. keys], "No feature called Gadgets"),
        ];

        foreach (var (args, fragment) in refusals)
        {
            ExpectRefusal(root, args, fragment, expect);
        }

        var several = Invoke(root, "invoices", "limits", "--keys", "x:nope, , Y:bool");

        expect(several.Error.Split('\n').Count(line => line.StartsWith("error:", StringComparison.Ordinal)) >= 4, "Several mistakes in one command were not all reported.");
    }

    private static void KeyCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        (string Keys, string Fragment)[] refused =
        [
            ("Max", "is not written Name:type"),
            ("max:int(1..2)=1", "is not a key name"),
            ("Max:float(1..2)=1", "is not a type of this tool"),
            ("Max:int=1", "needs its bounds"),
            ("Max:int(1..)=1", "needs its bounds"),
            ("Max:int(a..b)=1", "needs its bounds"),
            ("Max:int(5..1)=3", "min must be below max"),
            ("Max:int(1..9)", "needs its default"),
            ("Max:int(1..9)=10", "is not a whole number inside 1..9"),
            ("Max:int(1..9)=0", "is not a whole number inside 1..9"),
            ("Max:int(-2147483648..9)=1", "neither the extreme"),
            ("Max:int(1..2147483647)=1", "neither the extreme"),
            ("Max:int(1..9)=x", "is not a whole number inside"),
            ("Max:long(1..9)=\"1\"", "is not a whole number inside"),
            ("Span:duration(1..9)=2", "needs its bounds"),
            ("Span:duration(00:00:01..00:01:00)=00:02:00", "is not a whole-second duration inside"),
            ("Span:duration(00:00:00..00:01:00)=00:00:30", "needs its bounds"),
            ("Span:duration(00:00:01.5..00:01:00)=00:00:30", "needs its bounds"),
            ("Flag:bool", "needs its default"),
            ("Flag:bool=yes", "needs its default"),
            ("Flag:bool(1..2)=true", "has no bounds"),
            ("Token:secret=abc", "neither bounds nor a default"),
            ("Token:secret(1..5)", "neither bounds nor a default"),
            ("Name:string=abc", "needs its length range"),
            ("Name:string(0..9)", "minimum length must be 1 or more"),
            ("Name:string(1..9)=toolongdefault", "fits the length range"),
            ("Name:string(1..9)=a\"b", "letters, digits, spaces"),
            ("Name:string(1..9)=a\\b", "letters, digits, spaces"),
            ("Name:string(1..9)= a", "letters, digits, spaces"),
            ("Name:string(1..9)=a`b", "letters, digits, spaces"),
            ("Name:string(1..9)=a|b", "letters, digits, spaces"),
            ("Name:string(1..5000)=a", "max between 1 and 4096"),
            ("Max:int(1..9)=1, max:bool=true", "is not a key name"),
            ("Max:int(1..9)=1, MAX:bool=true", "more than once"),
            ("Class:int(1..9)=1", "C# keyword"),
            ("SectionName:int(1..9)=1", "C# keyword or a member"),
            ("Flag:bool=true", "holds only bool keys"),
        ];

        foreach (var (keys, fragment) in refused)
        {
            ExpectRefusal(root, ["Invoices", "Limits", "--keys", keys], fragment, expect);
        }

        (string Keys, string Json)[] accepted =
        [
            ("Max:int(-5..5)=-1", "\"Max\": -1"),
            ("Span:duration(1.00:00:00..2.00:00:00)=1.12:00:00, Flag:bool=false", "\"Span\": \"1.12:00:00\""),
            ("Name:string(1..20)=a b.c-d/e@f:g", "\"Name\": \"a b.c-d/e@f:g\""),
            ("Name:string(1..9)", "\"Name\": \"\""),
        ];

        foreach (var (keys, json) in accepted)
        {
            string tree = NewTree(scratch);
            var result = Invoke(tree, "Invoices", "Limits", "--keys", keys);

            expect(result.Code == 0, $"[--keys {keys}] was refused: {result.Error}");
            expect(Read(tree, _api).Contains(json, StringComparison.Ordinal), $"[--keys {keys}] left no {json} in the API's appsettings.json.");
        }
    }

    private static void WriteCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);
        var dry = Invoke(root, "Invoices", "InvoiceLimits", "--keys", _validKeys, "--dry-run");

        expect(dry.Code == 0 && dry.Output.Contains("Dry run: nothing was written.", StringComparison.Ordinal), $"The dry run failed: {dry.Error}");
        expect(dry.Output.Contains("InvoiceLimitsOptions.cs", StringComparison.Ordinal), "The dry run did not list what it would write.");
        expect(Same(before, Snapshot(root)), "The dry run changed the tree.");

        var result = Invoke(root, "Invoices", "InvoiceLimits", "--keys", _validKeys);

        expect(result.Code == 0, $"A valid setting was refused: {result.Error}");

        string options = "src/Application/A.Application/Features/Invoices/Configuration/Options/InvoiceLimitsOptions.cs";
        string tests = "tests/Application/A.Application.UnitTests/Features/Invoices/Configuration/InvoiceLimitsOptionsTests.cs";
        string source = Read(root, options);

        void Holds(string text, string fragment, string what) =>
            expect(text.ReplaceLineEndings("\n").Contains(fragment, StringComparison.Ordinal), $"{what} does not hold '{fragment}'.");

        Holds(source, "public const string SectionName = \"InvoiceLimits\";", "The options class");
        Holds(source, "public int MaxItems { get; set; } = 10;", "The options class");
        Holds(source, "public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);", "The options class");
        Holds(source, "public string Label { get; set; } = \"main\";", "The options class");
        Holds(source, "public string Owner { get; set; } = string.Empty;", "The options class");
        Holds(source, "public string ApiKey { get; set; } = string.Empty;", "The options class");
        Holds(source, "public long Cap { get; set; } = 1000;", "The options class");
        Holds(source, "if (options.MaxItems is < 1 or > 100)", "The validator");
        Holds(source, "failures.Add($\"'{section}:MaxItems' must be between 1 and 100.\");", "The validator");
        Holds(source, "if (options.Interval < TimeSpan.FromSeconds(1) || options.Interval > TimeSpan.FromHours(1))", "The validator");
        Holds(source, "if (options.Owner.Length is < 3 or > 40)", "The validator");
        Holds(source, "if (string.IsNullOrWhiteSpace(options.ApiKey))", "The validator");
        Holds(source, "if (options.Cap is < 1 or > 5000000000)", "The validator");
        expect(!source.Contains("Strict", StringComparison.Ordinal) || !source.Contains("options.Strict", StringComparison.Ordinal), "A bool key was given a check.");

        string module = Read(root, _module);

        Holds(module, "using Acme.Application.Features.Invoices.Configuration.Options;\n", "The module's usings");
        Holds(module, "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", "The module's usings");
        Holds(module, "        ArgumentNullException.ThrowIfNull(services);\n\n        services.AddOptions<InvoiceLimitsOptions>()\n            .BindConfiguration(InvoiceLimitsOptions.SectionName)\n            .ValidateOnStart();\n        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InvoiceLimitsOptions>, InvoiceLimitsOptionsValidator>());\n\n        services.AddFeature(\"Invoices\");", "The registration");

        foreach (string appsettings in new[] { _api, _worker })
        {
            string json = Read(root, appsettings);

            Holds(json, "\"InvoiceLimits\": {\n    \"MaxItems\": 10,\n    \"Interval\": \"00:05:00\",\n    \"Label\": \"main\",\n    \"Owner\": \"\",\n    \"ApiKey\": \"\",\n    \"Strict\": true,\n    \"Cap\": 1000\n  }", appsettings);

            try
            {
                using var document = JsonDocument.Parse(json);

                expect(document.RootElement.TryGetProperty("Logging", out _), $"{appsettings} lost a section it had.");
            }
            catch (JsonException exception)
            {
                expect(false, $"{appsettings} is not JSON any more: {exception.Message}");
            }
        }

        string guide = Read(root, _guide);

        Holds(guide, "## Invoices\n\n### `InvoiceLimits`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n| `MaxItems` | int | `10` | 1–100. |\n", "The configuration reference");
        Holds(guide, "| `Interval` | timespan | `00:05:00` | `00:00:01`–`01:00:00`. |", "The configuration reference");
        Holds(guide, "| `Owner` | string | — | **Required.** 3–40 characters, from the environment variable", "The configuration reference");
        Holds(guide, "| `ApiKey` | string | `\"\"` | **Required, secret.** Supplied by the environment variable `InvoiceLimits__ApiKey`", "The configuration reference");
        Holds(guide, "|\n\n## Beyond the sections", "The configuration reference");
        expect(guide.IndexOf("## Invoices", StringComparison.Ordinal) < guide.IndexOf("## Beyond the sections", StringComparison.Ordinal), "The new group is not before 'Beyond the sections'.");
        Holds(Read(root, _vocabulary), "            [\n                \"Configuration/Options\",\n                \"Helpers\",", "The folder vocabulary");

        Holds(Read(root, _composition), "            [\"Files:Categories:document:UploadModes:0\"] = \"Direct\",\n\n            [\"InvoiceLimits:Owner\"] = \"architecture-tests\",\n            [\"InvoiceLimits:ApiKey\"] = \"architecture-tests-secret\",\n        };", "The architecture tests' valid settings");
        Holds(Read(root, _compose), "  Jwt__Key: ${JWT_KEY:?set JWT_KEY in .env}\n\n  InvoiceLimits__Owner: ${INVOICE_LIMITS_OWNER:?set INVOICE_LIMITS_OWNER in .env}\n  InvoiceLimits__ApiKey: ${INVOICE_LIMITS_API_KEY:?set INVOICE_LIMITS_API_KEY in .env}\n\nservices:", "The Compose file");
        Holds(Read(root, _environment), "JOBS_ENABLED=true\n\n# InvoiceLimits: no default, so Compose refuses to start until each is set.\nINVOICE_LIMITS_OWNER=\nINVOICE_LIMITS_API_KEY=\n", "The environment example");
        expect(!Read(root, _compose).Contains("InvoiceLimits__MaxItems", StringComparison.Ordinal), "A key with a default was mapped in Compose.");
        Holds(guide, "from the environment variable `InvoiceLimits__Owner` (`INVOICE_LIMITS_OWNER` in `.env` for Compose)", "The configuration reference");

        string testSource = Read(root, tests);

        Holds(testSource, "public void MaxItems_PastItsBounds_NamesTheKey(int value)", "The tests");
        Holds(testSource, "[InlineData(0)]\n    [InlineData(101)]", "The tests");
        Holds(testSource, "public void ApiKey_WhenBlank_NamesTheKey(string value)", "The tests");
        Holds(testSource, "public void Owner_PastItsBounds_NamesTheKey(int length)", "The tests");
        Holds(testSource, "[InlineData(2)]\n    [InlineData(41)]", "The tests");
        Holds(testSource, "[InlineData(5000000001L)]", "The tests");
        Holds(testSource, "services.AddSingleton<IConfiguration>(configuration);\n        services.AddInvoices();", "The tests");
        Holds(testSource, "failure.ToString().ShouldContain(\"'InvoiceLimits:MaxItems'\");", "The tests");
        Holds(testSource, "bound.Interval.ShouldBe(TimeSpan.FromSeconds(1));", "The tests");
        Holds(testSource, "[\"InvoiceLimits:Owner\"] = new string('b', 3),", "The tests");
        Holds(testSource, "Owner = new string('a', 3),", "The tests");
        expect(!testSource.Contains("Strict_", StringComparison.Ordinal), "A bool key was given a boundary test.");

        foreach (string file in new[] { options, tests })
        {
            foreach (string finding in FileProblems(root, file))
            {
                expect(false, finding);
            }
        }

        string manifest = Read(root, ".feature-manifest/Invoices.json");

        Holds(manifest, options, "The feature's manifest");
        Holds(manifest, tests, "The feature's manifest");
        RecordedEditsUndo(root, before, expect);

        var again = Snapshot(root);
        var second = Invoke(root, "Invoices", "InvoiceLimits", "--keys", _validKeys);

        expect(second.Code == 1 && second.Error.Contains("is already bound by", StringComparison.Ordinal), "A second run was not refused for the section it already wrote.");
        expect(Same(again, Snapshot(root)), "A second run changed the tree.");

        var other = Invoke(root, "Invoices", "invoiceLimits", "--keys", _validKeys);

        expect(other.Code == 1, "A section differing only by its first letter's case was accepted.");

        var clash = Invoke(root, "Invoices", "InvoiceLimits:Sub", "--keys", _validKeys);

        expect(clash.Code == 1, "A section name holding a colon was accepted.");

        ExpectRefusal(root, ["Invoices", "INVOICELIMITS", "--keys", "Max:int(1..9)=1"], "already holds a section called", expect);
        ExpectRefusal(root, ["Notes", "NoteLimits", "--keys", "Max:int(1..9)=1"], "already has a heading for NoteLimits", expect);
    }

    private static IEnumerable<string> FileProblems(string root, string relative)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative));

        if (bytes is not [0xEF, 0xBB, 0xBF, ..])
        {
            yield return $"{relative}: no UTF-8 BOM, which the formatting gate refuses.";
        }

        string text = Decode(bytes);

        if (text.Contains('\r', StringComparison.Ordinal) || text.EndsWith("\n\n", StringComparison.Ordinal) || !text.EndsWith('\n'))
        {
            yield return $"{relative}: not LF endings closed by exactly one newline.";
        }

        if (text.Contains("//", StringComparison.Ordinal) || text.Contains("/*", StringComparison.Ordinal))
        {
            yield return $"{relative}: a comment, where generated code carries none.";
        }

        string[] usings = [.. text.Split('\n').TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal))];

        if (!usings.SequenceEqual(usings.OrderBy(UsingKey, StringComparer.Ordinal)))
        {
            yield return $"{relative}: its usings are not in order.";
        }

        if (!text.Contains($"namespace Acme.{(relative.StartsWith("tests", StringComparison.Ordinal) ? "Application.UnitTests" : "Application")}.Features.Invoices.", StringComparison.Ordinal))
        {
            yield return $"{relative}: its namespace is not its project plus its folders.";
        }
    }

    private static void DefaultsOnlyCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "Limits", "--keys", "Max:int(1..9)=1");

        expect(result.Code == 0, $"A setting with defaults only was refused: {result.Error}");

        foreach (string untouched in new[] { _compose, _environment, _composition })
        {
            expect(before[untouched] == Snapshot(root)[untouched], $"A setting with a default for every key rewrote {untouched}.");
        }
    }

    private static void ConfigurationParameterCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var result = Invoke(root, "Notes", "NoteRetention", "--keys", "Days:int(1..30)=7");

        expect(result.Code == 0, $"A feature taking the configuration was refused: {result.Error}");
        expect(
            Read(root, _module).Contains("            .Bind(configuration.GetSection(NoteRetentionOptions.SectionName))\n", StringComparison.Ordinal),
            "A feature that takes the configuration was not bound from it.");
        expect(!Read(root, _module).Contains("BindConfiguration", StringComparison.Ordinal), "A feature that takes the configuration was bound a second way.");
        expect(
            Read(root, "tests/Application/A.Application.UnitTests/Features/Notes/Configuration/NoteRetentionOptionsTests.cs").Contains("services.AddNotes(configuration);", StringComparison.Ordinal),
            "The tests did not pass the configuration the feature takes.");
        expect(Read(root, _api).Contains("\"NoteRetention\"", StringComparison.Ordinal), "The only host composing Notes did not get the section.");
        expect(!Read(root, _worker).Contains("NoteRetention", StringComparison.Ordinal), "A host that does not compose the feature got its section.");
    }

    private static void AnchorCases(string scratch, Action<bool, string> expect)
    {
        string[] args = ["Invoices", "Limits", "--keys", "Max:int(1..9)=1"];
        string[] secret = ["Invoices", "Limits", "--keys", "Max:int(1..9)=1, Token:secret"];

        (string Name, string[] Args, string File, Func<string, string> Damage, string Fragment)[] cases =
        [
            ("no method", args, _module, text => text.Replace("AddInvoices", "AddBills", StringComparison.Ordinal), "the registration in AddInvoices"),
            ("no guard", args, _module, text => text.Replace("ArgumentNullException.ThrowIfNull(services);\n\n        services.AddFeature(\"Invoices\")", "services.AddFeature(\"Invoices\")", StringComparison.Ordinal), "the registration in AddInvoices"),
            ("not an object", args, _api, _ => "[]\n", "the Limits section of"),
            ("no guide end", args, _guide, text => text.Replace("## Beyond the sections", "## Elsewhere", StringComparison.Ordinal), "the Limits section of"),
            ("no vocabulary list", args, _vocabulary, text => text.Replace("[\"src/Application/A.Application\"]", "[\"src/Application/A.Elsewhere\"]", StringComparison.Ordinal), "the Configuration/Options folder word of"),
            ("no compose anchor", secret, _compose, text => text.Replace("x-host-environment: &host-environment", "x-other: &other", StringComparison.Ordinal), "the x-host-environment of"),
            ("empty compose block", secret, _compose, text => text.Replace("  ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}\n\n  Jwt__Key: ${JWT_KEY:?set JWT_KEY in .env}\n", string.Empty, StringComparison.Ordinal), "the x-host-environment of"),
            ("empty environment example", secret, _environment, _ => string.Empty, "the variables of"),
            ("no valid settings", secret, _composition, text => text.Replace("ValidSettings", "Settings", StringComparison.Ordinal), "the ValidSettings of"),
            ("crlf", args, _api, text => text.ReplaceLineEndings("\r\n"), "CRLF line endings"),
            ("missing appsettings", args, _worker, _ => string.Empty, "is not there"),
        ];

        foreach (var (name, caseArgs, file, damage, fragment) in cases)
        {
            string root = NewTree(scratch);
            string full = Path.Combine(root, file);

            if (name == "missing appsettings")
            {
                File.Delete(full);
            }
            else
            {
                byte[] bytes = File.ReadAllBytes(full);
                string damaged = damage(Decode(bytes));

                File.WriteAllBytes(full, [.. bytes is [0xEF, 0xBB, 0xBF, ..] ? Encoding.UTF8.GetPreamble() : [], .. Encoding.UTF8.GetBytes(damaged)]);
            }

            ExpectRefusal(root, caseArgs, fragment, expect);
        }

        (string Name, Func<string, string> Damage)[] usings =
        [
            ("usings out of order", text => text.Replace("using Acme.Application.Core.Common.Events;\nusing Microsoft.Extensions.Configuration;", "using Microsoft.Extensions.Configuration;\nusing Acme.Application.Core.Common.Events;", StringComparison.Ordinal)),
            ("no usings", text => text[text.IndexOf("namespace", StringComparison.Ordinal)..]),
        ];
        string[] added = ["using Acme.Application.Features.Invoices.Configuration.Options;", "using Microsoft.Extensions.DependencyInjection.Extensions;", "using Microsoft.Extensions.Options;"];

        foreach (var (name, damage) in usings)
        {
            string root = NewTree(scratch);
            string full = Path.Combine(root, _module);

            File.WriteAllBytes(full, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(damage(Decode(File.ReadAllBytes(full))))]);

            var result = Invoke(root, args);
            string[] lines = Read(root, _module).Split('\n');

            expect(
                result.Code == 0 && added.All(line => lines.Count(candidate => candidate == line) == 1),
                $"[{name}] the setting's usings were not each added once, beside the usings the module holds: {result.Error}");
        }
    }

    private static void TreeCases(string scratch, Action<bool, string> expect)
    {
        string unhosted = NewTree(scratch);

        File.Delete(Path.Combine(unhosted, "src/Presentation/A.Worker/Program.cs"));
        File.WriteAllText(Path.Combine(unhosted, "src/Presentation/A.Api/Program.cs"), "builder.Services.AddNotes(builder.Configuration);\n");
        ExpectRefusal(unhosted, ["Invoices", "Limits", "--keys", "Max:int(1..9)=1"], "No host composes Invoices", expect);

        string template = NewTree(scratch);
        string module = Path.Combine(template, _module);

        File.WriteAllText(module, "//" + ProjectDocuments.ConditionOpen + "files)\n" + File.ReadAllText(module).Replace("\uFEFF", string.Empty, StringComparison.Ordinal) + "//" + ProjectDocuments.ConditionClose + "\n");
        ExpectRefusal(template, ["Invoices", "Limits", "--keys", "Max:int(1..9)=1"], "template's own tree", expect);

        string withoutTests = NewTree(scratch);

        Directory.Delete(Path.Combine(withoutTests, "tests/Application"), recursive: true);

        var untested = Invoke(withoutTests, "Invoices", "Limits", "--keys", "Max:int(1..9)=1");

        expect(untested.Code == 0, $"A tree without an application test project was refused: {untested.Error}");
        expect(!Directory.Exists(Path.Combine(withoutTests, "tests/Application")), "Tests were written to a tree that has no project for them.");
    }

    private static void UnrecordableEditCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        File.WriteAllText(Path.Combine(root, _environment), File.ReadAllText(Path.Combine(root, _environment)).TrimEnd('\n'));
        ExpectRefusal(root, ["Invoices", "InvoiceLimits", "--keys", _validKeys], "cannot be recorded to be undone", expect);

        File.Delete(Path.Combine(root, ".feature-manifest/Invoices.json"));

        var unrecorded = Invoke(root, "Invoices", "InvoiceLimits", "--keys", _validKeys);

        expect(unrecorded.Code == 0, $"A feature with no manifest, which records nothing, was refused for an edit that ends the file with a newline: {unrecorded.Error}");
    }

    private static void RollbackCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        File.WriteAllText(Path.Combine(root, "tests/Application/A.Application.UnitTests/Features"), "not a folder\n");

        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "Limits", "--keys", "Max:int(1..9)=1");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), $"A write that failed midway did not say so: {result.Error}");
        expect(Same(before, Snapshot(root)), "A write that failed midway left files or edits behind.");

        string edited = NewTree(scratch);
        string guide = Path.Combine(edited, _guide);
        byte[] original = File.ReadAllBytes(guide);
        bool restored = false;

        try
        {
            TextEdits.Commit(edited, [("src/written.cs", [1])], [(_guide, original, [2]), ("docs", [], [3])]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            restored = File.ReadAllBytes(guide).AsSpan().SequenceEqual(original) && !File.Exists(Path.Combine(edited, "src/written.cs"));
        }

        expect(restored, "An edit that could not be written did not put back the files edited before it and remove the files written.");
    }
}
