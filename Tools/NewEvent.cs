#:property UseAppHost=false

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class NewEvent
{
    private const string _manifestFolder = ".feature-manifest";

    private const string _conditionOpen = "#" + "if (";

    private const string _conditionClose = "#" + "endif";

    private const string _suffix = "DomainEvent";

    private const string _usage =
        "Usage: dotnet run Tools/NewEvent.cs <Features> <Event> (--consumer <Features>:<Reaction> | --unconsumed \"<reason>\")\n"
        + "           [--fields \"Name:Type, Other:Type\"] [--dry-run] [--root <path>]\n"
        + "  <Features>    the folder of an existing feature of the rich shape, which raises the event, e.g. Invoices\n"
        + "  <Event>       PascalCase, past tense, ending in DomainEvent, e.g. InvoicePaidDomainEvent\n"
        + "  --consumer    the feature that reacts, and what it does: Reminders:CancelReminders writes\n"
        + "                CancelRemindersOnInvoicePaidConsumer in Reminders and registers it. Its body is yours:\n"
        + "                it re-derives its precondition and changes nothing when it runs twice (ADR 0006).\n"
        + "  --unconsumed  instead of a consumer, the reason nothing listens, written beside the event's name in\n"
        + "                DomainEventTests. One of the two is required: an event is consumed or listed.\n"
        + "  --fields      what the event carries besides OccurredOn, which it always ends with. Name:Type, a type\n"
        + "                of Guid, string, int, long, bool, decimal, DateOnly or DateTimeOffset. By default the\n"
        + "                id of the feature's aggregate, when it has exactly one repository.\n"
        + "  --dry-run     print what would be written, and write nothing.\n"
        + "  --root        the tree to write into, default the current directory.\n"
        + "       dotnet run Tools/NewEvent.cs --self-test";

    private static readonly string[] _optionNames = ["--consumer", "--unconsumed", "--fields", "--root", "--dry-run"];

    private static readonly string[] _fieldTypes = ["Guid", "string", "int", "long", "bool", "decimal", "DateOnly", "DateTimeOffset"];

    private static readonly string[] _keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ];

    private static readonly Regex _pascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex _usingLine = new(@"^using [A-Z][\w.]*;$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private sealed record Emission(string Path, string Content);

    private sealed record Edit(string Path, string Description, Func<string, string?> Apply);

    private sealed record Request(string? Features, string? Name, string? Consumer, string? Unconsumed, string? Fields, string Root, bool DryRun);

    private sealed record Field(string Name, string Type);

    private sealed record Spec(
        string Prefix,
        string Features,
        string Name,
        IReadOnlyList<Field> Fields,
        string? ConsumerFeatures,
        string? Reaction,
        bool ConsumerTakesConfiguration,
        string? Unconsumed,
        string? Manifest)
    {
        internal string Base => Name[..^_suffix.Length];

        internal string? ConsumerName => Reaction is null ? null : $"{Reaction}On{Base}Consumer";

        internal string EventNamespace => $"{Prefix}.Domain.Features.{Features}.Model.Events";

        internal string ConsumerNamespace => $"{Prefix}.Application.Features.{ConsumerFeatures}.Services.Consumers.{Base}";
    }

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(_usage);

            return 0;
        }

        return Run(args, Console.Out, Console.Error);
    }

    private static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var problems = new List<string>();
        var request = Parse(args, problems);
        string root = Path.GetFullPath(request.Root);
        var spec = problems.Count == 0 ? Check(request, root, problems) : null;

        if (spec is null)
        {
            return Refuse(error, problems);
        }

        var emissions = Emissions(spec, root);
        string[] existing = [.. emissions.Select(emission => emission.Path).Where(path => File.Exists(Path.Combine(root, path)))];

        if (existing.Length > 0)
        {
            problems.Add($"{existing.Length} of the files to write already exist, and none is ever overwritten:\n" + string.Join("\n", existing.Select(path => $"  {path}")));

            return Refuse(error, problems);
        }

        var (edits, documents) = Edits(spec, root, problems);

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        var changed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var edit in edits)
        {
            string current = changed.TryGetValue(edit.Path, out string? pending) ? pending : documents[edit.Path];
            string? next = edit.Apply(current);

            if (next is null)
            {
                problems.Add($"{edit.Description}: its anchor is not in {edit.Path}, so nothing could be written. Fix that file, or make this edit by hand.");

                continue;
            }

            changed[edit.Path] = next;
        }

        foreach (string path in changed.Where(pair => pair.Value == documents[pair.Key]).Select(pair => pair.Key).ToList())
        {
            changed.Remove(path);
        }

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        Emission? manifest = spec.Manifest is null ? null : ManifestWith(root, spec.Manifest, emissions);

        if (!request.DryRun)
        {
            try
            {
                WriteAll(root, emissions, changed, manifest);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

                return 1;
            }
        }

        Report(output, spec, emissions, changed.Keys, request.DryRun);

        return 0;
    }

    private static Request Parse(string[] args, List<string> problems)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        bool dryRun = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (!argument.StartsWith('-'))
            {
                positional.Add(argument);

                continue;
            }

            string name = argument;
            string? value = null;
            int equals = argument.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                name = argument[..equals];
                value = argument[(equals + 1)..];
            }

            if (!_optionNames.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"'{name}' is not an option. The options are {string.Join(", ", _optionNames)}.");

                continue;
            }

            if (name == "--dry-run")
            {
                dryRun = true;

                continue;
            }

            if (value is null)
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    problems.Add($"'{name}' needs a value.");

                    continue;
                }

                value = args[++index];
            }

            if (!options.TryAdd(name, value))
            {
                problems.Add($"'{name}' is given twice; it takes one value.");
            }
        }

        if (positional.Count != 2)
        {
            problems.Add($"Expected two names, <Features> and <Event>, and found {positional.Count}. The feature's folder, then the event: Invoices InvoicePaidDomainEvent.");
        }

        return new Request(
            positional.Count > 0 ? positional[0] : null,
            positional.Count > 1 ? positional[1] : null,
            options.GetValueOrDefault("--consumer"),
            options.GetValueOrDefault("--unconsumed"),
            options.GetValueOrDefault("--fields"),
            options.GetValueOrDefault("--root") ?? ".",
            dryRun);
    }

    private static Spec? Check(Request request, string root, List<string> problems)
    {
        string features = request.Features ?? string.Empty;
        string name = request.Name ?? string.Empty;

        if (!_pascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!_pascalCase.IsMatch(name))
        {
            problems.Add($"'{name}' is not an event name: <Event> is PascalCase, e.g. InvoicePaidDomainEvent.");
        }
        else if (!name.EndsWith(_suffix, StringComparison.Ordinal) || name.Length == _suffix.Length)
        {
            problems.Add($"'{name}' does not end in {_suffix} after a name of its own: every event of a feature does, as TodoItemCompletedDomainEvent does. Name the fact, e.g. InvoicePaid{_suffix}.");
        }
        else if (name.Length > 60)
        {
            problems.Add("An event name longer than 60 characters makes a path that some tools cannot open. Shorten it.");
        }

        if (request.Consumer is null == (request.Unconsumed is null))
        {
            problems.Add(
                request.Consumer is null
                    ? "Say who listens: --consumer <Features>:<Reaction>, or --unconsumed \"<reason>\" when nothing does. An event is consumed or listed as unconsumed with its reason (ADR 0006), and one of the two is required."
                    : "--consumer and --unconsumed exclude each other: an event with a consumer is not listed as unconsumed.");
        }

        if (request.Unconsumed is { } reason && (reason.Trim().Length < 8 || reason.Length > 120 || reason.Any(char.IsControl) || reason.Contains("//", StringComparison.Ordinal)))
        {
            problems.Add("--unconsumed is the reason in one line of 8 to 120 characters, with no '//' in it, written beside the event's name as a comment.");
        }

        string? consumerFeatures = null;
        string? reaction = null;

        if (request.Consumer is { } consumer)
        {
            string[] parts = consumer.Split(':');

            if (parts.Length != 2 || !_pascalCase.IsMatch(parts[0]) || !_pascalCase.IsMatch(parts[1]))
            {
                problems.Add($"--consumer '{consumer}' is not written <Features>:<Reaction>, both PascalCase, e.g. Reminders:CancelReminders.");
            }
            else
            {
                (consumerFeatures, reaction) = (parts[0], parts[1]);

                if (reaction.Length > 40 || reaction.EndsWith("Consumer", StringComparison.Ordinal))
                {
                    problems.Add($"'{reaction}' is what the consumer does, a verb and its subject of at most 40 characters without 'Consumer': the class is {reaction}On<Event>Consumer.");
                }
            }
        }

        if (problems.Count > 0)
        {
            return null;
        }

        string prefix;

        try
        {
            prefix = ProductPrefix(root);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }

        string domain = Path.Combine(root, "Src", "Domain", FolderOf(prefix, "Domain"), "Features", features);

        if (!Directory.Exists(domain))
        {
            problems.Add(
                $"{features} has no domain folder 'Src/Domain/{FolderOf(prefix, "Domain")}/Features/{features}', so it is a service over a store and raises no domain event. "
                + "Promote it to an aggregate first (docs/architecture/feature-shapes.md, From simple to rich).");

            return null;
        }

        var fields = ReadFields(request.Fields, name, domain, features, problems);

        if (problems.Count > 0)
        {
            return null;
        }

        if (SourceFiles(Path.Combine(root, "Src"), "*.cs").Any(file => Regex.IsMatch(File.ReadAllText(file), $@"\b(?:record|class) {name}\b", RegexOptions.None, TimeSpan.FromSeconds(2))))
        {
            problems.Add($"A type called {name} already exists under Src. An event is written once; after that it is edited.");

            return null;
        }

        bool takes = false;

        if (consumerFeatures is not null)
        {
            string application = Path.Combine(root, "Src", "Application", FolderOf(prefix, "Application"));

            if (!Directory.Exists(Path.Combine(application, "Features", consumerFeatures)))
            {
                problems.Add($"No feature called {consumerFeatures} to react: 'Src/Application/{FolderOf(prefix, "Application")}/Features/{consumerFeatures}' does not exist.");

                return null;
            }

            string module = Path.Combine(application, "ApplicationModule.cs");

            if (File.Exists(module) && Regex.Match(File.ReadAllText(module), $@"public static IServiceCollection Add{consumerFeatures}\(this IServiceCollection services(?<rest>[^)]*)\)", RegexOptions.None, TimeSpan.FromSeconds(2)) is { Success: true } method)
            {
                takes = Regex.IsMatch(method.Groups["rest"].Value, @"IConfiguration configuration\b", RegexOptions.None, TimeSpan.FromSeconds(1));
            }

            string consumerFile = Path.Combine(application, "Features", consumerFeatures, "Services", "Consumers", name[..^_suffix.Length], $"{reaction}On{name[..^_suffix.Length]}Consumer.cs");

            if (File.Exists(consumerFile))
            {
                problems.Add($"{Path.GetRelativePath(root, consumerFile).Replace('\\', '/')} exists. A consumer is written once; after that it is edited.");

                return null;
            }
        }

        string manifest = $"{_manifestFolder}/{features}.json";
        string manifestFull = Path.Combine(root, manifest);

        if (File.Exists(manifestFull) && !ManifestReads(manifestFull, features))
        {
            problems.Add($"'{manifest}' does not read as the manifest of {features}, so the files written could not be recorded in it. Repair or delete it first.");

            return null;
        }

        return new Spec(prefix, features, name, fields, consumerFeatures, reaction, takes, request.Unconsumed?.Trim(), File.Exists(manifestFull) ? manifest : null);
    }

    private static List<Field> ReadFields(string? text, string name, string domain, string features, List<string> problems)
    {
        var fields = new List<Field>();

        if (text is null)
        {
            string repositories = Path.Combine(domain, "Repositories");
            string[] found = Directory.Exists(repositories)
                ? [.. Directory.EnumerateFiles(repositories, "I*Repository.cs")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>()
                    .Select(file => file[1..^"Repository".Length])
                    .Where(type => _pascalCase.IsMatch(type))
                    .Order(StringComparer.Ordinal)]
                : [];

            if (found.Length != 1)
            {
                problems.Add(
                    $"--fields is needed: {features} declares {(found.Length == 0 ? "no repository" : "several repositories, " + string.Join(", ", found))} under Repositories/, so there is no single aggregate whose id the event carries by default. "
                    + "Pass --fields \"InvoiceId:Guid\".");

                return fields;
            }

            return [new Field(found[0] + "Id", "Guid")];
        }

        foreach (string entry in text.Split(',').Select(entry => entry.Trim()))
        {
            string[] parts = entry.Split(':');

            if (entry.Length == 0 || parts.Length != 2)
            {
                problems.Add($"'{entry}' in --fields is not written Name:Type.");

                continue;
            }

            (string field, string type) = (parts[0].Trim(), parts[1].Trim());

            if (!_pascalCase.IsMatch(field))
            {
                problems.Add($"'{field}' in --fields is not a field name: PascalCase, e.g. InvoiceId.");
            }
            else if (field is "OccurredOn" or "EqualityContract" || field == name || _keywords.Contains(Lower(field), StringComparer.Ordinal))
            {
                problems.Add($"'{field}' in --fields is a name the event declares itself, or a C# keyword once lowered.");
            }
            else if (fields.Any(existing => string.Equals(existing.Name, field, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"--fields names {field} more than once.");
            }
            else if (!_fieldTypes.Contains(type, StringComparer.Ordinal))
            {
                problems.Add($"'{type}' in --fields is not a type of this tool. The types are {string.Join(", ", _fieldTypes)}; an event carries facts, not objects.");
            }
            else
            {
                fields.Add(new Field(field, type));
            }
        }

        if (problems.Count == 0 && fields.Count > 12)
        {
            problems.Add("An event carries at most 12 fields; one that needs more is a record to look up by id.");
        }

        return fields;
    }

    private static List<Emission> Emissions(Spec spec, string root)
    {
        string domain = $"Src/Domain/{FolderOf(spec.Prefix, "Domain")}/Features/{spec.Features}/Model/Events";
        var emissions = new List<Emission> { new($"{domain}/{spec.Name}.cs", EventRecord(spec)) };
        string domainTests = $"Tests/Domain/{FolderOf(spec.Prefix, "Domain.UnitTests")}";

        if (Directory.Exists(Path.Combine(root, domainTests)))
        {
            emissions.Add(new Emission($"{domainTests}/Features/{spec.Features}/Model/Events/{spec.Name}Tests.cs", EventTests(spec)));
        }

        if (spec.ConsumerFeatures is not null)
        {
            string application = $"Src/Application/{FolderOf(spec.Prefix, "Application")}/Features/{spec.ConsumerFeatures}/Services/Consumers/{spec.Base}";

            emissions.Add(new Emission($"{application}/{spec.ConsumerName}.cs", ConsumerClass(spec)));

            string applicationTests = $"Tests/Application/{FolderOf(spec.Prefix, "Application.UnitTests")}";

            if (Directory.Exists(Path.Combine(root, applicationTests)))
            {
                emissions.Add(new Emission($"{applicationTests}/Features/{spec.ConsumerFeatures}/Services/Consumers/{spec.Base}/{spec.ConsumerName}Tests.cs", ConsumerTests(spec)));
            }
        }

        return emissions;
    }

    private static string EventRecord(Spec spec)
    {
        string parameters = string.Concat(spec.Fields.Select(field => $"    {field.Type} {field.Name},\n"));

        return Sorted(
            $"using {spec.Prefix}.Domain.Core.Common.Events;\n\n"
            + $"namespace {spec.EventNamespace};\n\n"
            + $"public sealed record {spec.Name}(\n{parameters}    DateTimeOffset OccurredOn) : IDomainEvent;\n");
    }

    private static string Sample(Field field, int index) => field.Type switch
    {
        "Guid" => $"guid{index}",
        "string" => $"\"{Words(field.Name)}\"",
        "int" => "1",
        "long" => "1L",
        "bool" => "true",
        "decimal" => "1m",
        "DateOnly" => "new DateOnly(2026, 1, 1)",
        _ => "new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)",
    };

    private static string Words(string pascal) =>
        string.Concat(pascal.Select((character, index) => char.IsUpper(character) && index > 0 ? " " + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));

    private static string GuidDeclarations(Spec spec) =>
        string.Concat(spec.Fields.Select((field, index) => (field, index)).Where(pair => pair.field.Type == "Guid").Select(pair => $"        var guid{pair.index} = Guid.CreateVersion7();\n"));

    private static string EventTests(Spec spec)
    {
        string declarations = GuidDeclarations(spec);
        string arguments = string.Join(", ", spec.Fields.Select(Sample).Append("_occurredOn"));
        string assertions = string.Concat(spec.Fields.Select((field, index) => $"        domainEvent.{field.Name}.ShouldBe({Sample(field, index)});\n"));

        return Sorted(
            $"using {spec.Prefix}.Domain.Core.Common.Events;\n"
            + $"using {spec.EventNamespace};\n"
            + "using Shouldly;\nusing Xunit;\n\n"
            + $"namespace {spec.Prefix}.Domain.UnitTests.Features.{spec.Features}.Model.Events;\n\n"
            + $"public sealed class {spec.Name}Tests\n{{\n"
            + "    private static readonly DateTimeOffset _occurredOn = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);\n\n"
            + "    [Fact]\n    public void TheEvent_CarriesWhatItWasGiven()\n    {\n"
            + declarations + (declarations.Length > 0 ? "\n" : string.Empty)
            + $"        var domainEvent = new {spec.Name}({arguments});\n\n"
            + assertions
            + "        domainEvent.OccurredOn.ShouldBe(_occurredOn);\n    }\n\n"
            + $"    [Fact]\n    public void TheEvent_IsADomainEvent() =>\n        typeof(IDomainEvent).IsAssignableFrom(typeof({spec.Name})).ShouldBeTrue();\n}}\n");
    }

    private static string ConsumerClass(Spec spec) =>
        Sorted(
            $"using {spec.Prefix}.Application.Core.Common.Events;\n"
            + $"using {spec.EventNamespace};\n\n"
            + $"namespace {spec.ConsumerNamespace};\n\n"
            + $"internal sealed class {spec.ConsumerName} : IDomainEventConsumer<{spec.Name}>\n{{\n"
            + $"    public Task ConsumeAsync(\n        {spec.Name} domainEvent,\n        CancellationToken cancellationToken = default)\n    {{\n"
            + "        ArgumentNullException.ThrowIfNull(domainEvent);\n\n"
            + $"        throw new NotImplementedException(\n            \"{spec.Reaction} is not written yet: re-derive the precondition from the stores, act, then commit through IUnitOfWork, so that running twice changes nothing.\");\n"
            + "    }\n}\n");

    private static string ConsumerTests(Spec spec)
    {
        string call = spec.ConsumerTakesConfiguration
            ? $"services.Add{spec.ConsumerFeatures}(new ConfigurationBuilder().Build());"
            : $"services.Add{spec.ConsumerFeatures}();";
        string configuration = spec.ConsumerTakesConfiguration ? "using Microsoft.Extensions.Configuration;\n" : string.Empty;
        return Sorted(
            $"using {spec.Prefix}.Application.Core.Common.Events;\n"
            + $"using {spec.Prefix}.Application.Features.{spec.ConsumerFeatures}.Services.Consumers.{spec.Base};\n"
            + $"using {spec.EventNamespace};\n"
            + configuration
            + "using Microsoft.Extensions.DependencyInjection;\nusing Shouldly;\nusing Xunit;\n\n"
            + $"namespace {spec.Prefix}.Application.UnitTests.Features.{spec.ConsumerFeatures}.Services.Consumers.{spec.Base};\n\n"
            + $"public sealed class {spec.ConsumerName}Tests\n{{\n"
            + "    [Fact]\n"
            + $"    public void TheConsumer_IsRegisteredForItsEvent()\n    {{\n        var services = new ServiceCollection();\n        {call}\n\n"
            + $"        services.ShouldContain(descriptor =>\n            descriptor.ServiceType == typeof(IDomainEventConsumer<{spec.Name}>)\n            && descriptor.ImplementationType == typeof({spec.ConsumerName}));\n    }}\n\n"
            + "    [Fact]\n"
            + $"    public async Task AMissingEvent_IsRefused()\n    {{\n        var consumer = new {spec.ConsumerName}();\n\n"
            + "        await Should.ThrowAsync<ArgumentNullException>(() => consumer.ConsumeAsync(null!, TestContext.Current.CancellationToken));\n    }\n\n"
            + "    [Fact]\n"
            + $"    public void TheReaction_IsWritten() =>\n        Assert.Fail(\n            \"{spec.Reaction} is not written: {spec.ConsumerName} throws NotImplementedException, and the dispatcher logs that and drops the event. \"\n            + \"Re-derive the precondition from the stores, act, commit through IUnitOfWork so that running twice changes nothing (ADR 0006), \"\n            + \"then replace this test with the ones that hold the reaction.\");\n}}\n");
    }

    private static (List<Edit> Edits, Dictionary<string, string> Documents) Edits(Spec spec, string root, List<string> problems)
    {
        var edits = new List<Edit>();
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);

        string Load(string relative)
        {
            if (documents.TryGetValue(relative, out string? loaded))
            {
                return loaded;
            }

            string full = Path.Combine(root, relative);

            if (!File.Exists(full))
            {
                problems.Add($"{relative} is not there, and the event needs it. Nothing is written until it is.");

                return string.Empty;
            }

            string text = Decode(File.ReadAllBytes(full));

            if (text.Contains('\r', StringComparison.Ordinal))
            {
                problems.Add($"{relative} has CRLF line endings; this project keeps LF. Run format-fix first.");
            }

            if (text.Contains(_conditionOpen, StringComparison.Ordinal))
            {
                problems.Add($"{relative} still holds template conditions: this is the template's own tree, not a generated project. Generate a project, then run this there.");
            }

            documents[relative] = text;

            return text;
        }

        if (spec.ConsumerFeatures is not null)
        {
            string module = $"Src/Application/{FolderOf(spec.Prefix, "Application")}/ApplicationModule.cs";
            string moduleText = Load(module);

            if (moduleText.Contains(spec.ConsumerName!, StringComparison.Ordinal))
            {
                problems.Add($"{module} already mentions {spec.ConsumerName}. A consumer is registered once.");
            }

            edits.Add(new Edit(module, $"the registration in Add{spec.ConsumerFeatures} of {module}", text => Register(text, spec)));
            edits.Add(new Edit(module, $"the usings of {module}", text => AddUsings(text, [$"{spec.Prefix}.Application.Core", spec.EventNamespace, spec.ConsumerNamespace])));
        }
        else
        {
            string list = $"Tests/Architecture/{FolderOf(spec.Prefix, "Architecture.Tests")}/Rules/DomainEventTests.cs";

            if (!File.Exists(Path.Combine(root, list)))
            {
                problems.Add($"{list} is not there: it holds the closed list of events nobody consumes, which an unconsumed event joins.");
            }
            else
            {
                Load(list);
                edits.Add(new Edit(list, $"the _deliberatelyUnconsumed list of {list}", text => Excuse(text, spec)));
            }
        }

        return (edits, documents);
    }

    private static string? Register(string text, Spec spec)
    {
        var method = new Regex($@"public static IServiceCollection Add{spec.ConsumerFeatures}\(this IServiceCollection services[^)]*\)\s*\{{\n", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(text);

        if (!method.Success)
        {
            return null;
        }

        int start = method.Index + method.Length;
        int next = text.IndexOf("\n    public static ", start, StringComparison.Ordinal);
        int end = text.IndexOf("        return services;\n", start, StringComparison.Ordinal);

        if (end < 0 || (next >= 0 && end > next) || !text[..end].EndsWith("\n\n", StringComparison.Ordinal))
        {
            return null;
        }

        return text.Insert(end, $"        services.AddDomainEventConsumer<{spec.Name}, {spec.ConsumerName}>();\n\n");
    }

    private static string? Excuse(string text, Spec spec)
    {
        const string opening = "private static readonly string[] _deliberatelyUnconsumed =\n    [\n";
        int start = text.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        int close = text.IndexOf("\n    ];\n", start + opening.Length - 1, StringComparison.Ordinal);

        if (close < 0 || text[start..close].Contains($"\"{spec.Name}\"", StringComparison.Ordinal))
        {
            return null;
        }

        return text.Insert(close + 1, $"        // {spec.Unconsumed}\n        \"{spec.Name}\",\n");
    }

    private static void Report(TextWriter output, Spec spec, List<Emission> emissions, IEnumerable<string> edited, bool dryRun)
    {
        output.WriteLine(dryRun ? "Dry run: nothing was written." : $"Wrote the event {spec.Name} of {spec.Features}.");
        output.WriteLine();
        output.WriteLine($"{(dryRun ? "Would write" : "Wrote")} {emissions.Count} file(s):");

        foreach (var emission in emissions)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();
        output.WriteLine($"{(dryRun ? "Would edit" : "Edited")}:");

        foreach (string path in edited.Order(StringComparer.Ordinal))
        {
            output.WriteLine($"  {path}");
        }

        output.WriteLine();
        output.WriteLine("What is left to you:");
        output.WriteLine($"  - Raise it: call RaiseDomainEvent(new {spec.Name}(...)) in the aggregate method that makes the fact true; nothing publishes an event no aggregate raises.");
        output.WriteLine("  - Test that method: the event is among the aggregate's DomainEvents after the call, with the values it was given.");

        if (spec.ConsumerFeatures is not null)
        {
            output.WriteLine($"  - Write {spec.ConsumerName}: it throws NotImplementedException until you do, and the dispatcher would log that and drop every event. So its generated test TheReaction_IsWritten fails, and verify and CI stay red until you replace it with the tests of the reaction.");
            output.WriteLine("    It re-derives its precondition when it runs, so running twice is running once (ADR 0006); a fact whose loss would matter has a sweep.");
            output.WriteLine($"    {spec.ConsumerFeatures} reaches {spec.Features} only through its events: use {spec.EventNamespace} and nothing else of it.");
        }
        else
        {
            output.WriteLine("  - The reason nothing listens is in DomainEventTests beside the name; when a consumer is written later, delete that entry (NoEventIsListedAsUnconsumed_WhileSomethingConsumesIt fails until you do).");
        }
    }

    private static int Refuse(TextWriter error, List<string> problems)
    {
        foreach (string problem in problems)
        {
            error.WriteLine($"error: {problem}");
        }

        error.WriteLine();
        error.WriteLine(_usage);
        error.WriteLine();
        error.WriteLine("Nothing was written.");

        return 1;
    }

    private static string ProductPrefix(string root)
    {
        string projectFile = Path.Combine(root, "cleanarch.json");

        if (!File.Exists(projectFile))
        {
            throw new InvalidOperationException(
                $"No cleanarch.json in '{root}', the file that records the project's name. Run this from the root of a tree generated by this template, or name that root with --root.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(projectFile));

        return document.RootElement.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } prefix
            ? prefix
            : throw new InvalidOperationException($"'{projectFile}' records no name.");
    }

    private static string FolderOf(string prefix, string project) =>
        string.Concat(prefix.Split('.').Select(SegmentInitials)) + "." + project;

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static bool ManifestReads(string manifestFull, string features)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(manifestFull)) is JsonObject top
                && top["feature"]?.GetValue<string>() == features
                && top["files"] is JsonArray;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static string Decode(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes, bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0, bytes.Length - (bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0));

    private static byte[] Bytes(string path, string content, bool bom)
    {
        string text = content.ReplaceLineEndings("\n");

        return bom || path.EndsWith(".cs", StringComparison.Ordinal)
            ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]
            : Encoding.UTF8.GetBytes(text);
    }

    private static string HashOf(Emission emission) =>
        Convert.ToHexStringLower(SHA256.HashData(Bytes(emission.Path, emission.Content, bom: false)));

    private static Emission ManifestWith(string root, string manifest, List<Emission> plan)
    {
        var top = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(root, manifest)))!;
        var files = (JsonArray)top["files"]!;

        var entries = files
            .OfType<JsonObject>()
            .Select(file => (Path: file["path"]!.GetValue<string>(), Sha256: file["sha256"]!.GetValue<string>()))
            .Concat(plan.Select(emission => (emission.Path, Sha256: HashOf(emission))))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        var merged = new JsonArray();

        foreach (var (path, sha256) in entries)
        {
            merged.Add((JsonNode)new JsonObject { ["path"] = path, ["sha256"] = sha256 });
        }

        top["files"] = merged;

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            top.WriteTo(writer);
        }

        return new Emission(manifest, Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static void WriteAll(string root, List<Emission> emissions, Dictionary<string, string> changed, Emission? manifest)
    {
        var created = new List<string>();
        var directories = new List<string>();
        var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        try
        {
            foreach (var emission in emissions)
            {
                string full = Path.Combine(root, emission.Path);
                string directory = Path.GetDirectoryName(full)!;
                var missing = new Stack<string>();

                for (string next = directory; !Directory.Exists(next); next = Path.GetDirectoryName(next)!)
                {
                    missing.Push(next);
                }

                directories.AddRange(missing);
                Directory.CreateDirectory(directory);

                using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);

                stream.Write(Bytes(emission.Path, emission.Content, bom: false));
                created.Add(full);
            }

            var replacements = changed.Select(pair => (pair.Key, Content: pair.Value)).ToList();

            if (manifest is not null)
            {
                replacements.Add((manifest.Path, manifest.Content));
            }

            foreach (var (relative, content) in replacements)
            {
                string full = Path.Combine(root, relative);
                byte[] before = File.ReadAllBytes(full);
                string staged = full + ".new";

                originals[full] = before;
                File.WriteAllBytes(staged, Bytes(relative, content, before is [0xEF, 0xBB, 0xBF, ..]));
                File.Move(staged, full, overwrite: true);
            }
        }
        catch
        {
            foreach (var (full, before) in originals)
            {
                File.WriteAllBytes(full, before);

                if (File.Exists(full + ".new"))
                {
                    File.Delete(full + ".new");
                }
            }

            foreach (string file in created.Where(File.Exists))
            {
                File.Delete(file);
            }

            foreach (string directory in Enumerable.Reverse(directories))
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            throw;
        }
    }

    private static string Sorted(string source)
    {
        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        int usings = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        string[] sorted = [.. lines[..usings].Distinct(StringComparer.Ordinal).OrderBy(UsingKey, StringComparer.Ordinal)];

        return string.Join('\n', [.. sorted, .. lines[usings..]]).TrimEnd('\n') + "\n";
    }

    private static string UsingKey(string line) => (line.StartsWith("using System", StringComparison.Ordinal) ? "0" : "1") + line.TrimEnd(';').ToUpperInvariant();

    private static string? AddUsings(string text, string[] namespaces)
    {
        string[] lines = text.Split('\n');
        int count = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        if (count == 0 || lines[..count].Any(line => !_usingLine.IsMatch(line)))
        {
            return null;
        }

        string[] current = lines[..count];

        if (!current.SequenceEqual(current.OrderBy(UsingKey, StringComparer.Ordinal)))
        {
            return null;
        }

        string[] added = [.. current.Concat(namespaces.Select(name => $"using {name};")).Distinct(StringComparer.Ordinal).OrderBy(UsingKey, StringComparer.Ordinal)];

        return string.Join('\n', [.. added, .. lines[count..]]);
    }

    private static IEnumerable<string> SourceFiles(string folder, string pattern) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            : [];

    private static int SelfTest()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "newevent-selftest-" + Guid.CreateVersion7().ToString("N"));
        var problems = new List<string>();
        int checks = 0;

        void Expect(bool condition, string message)
        {
            checks++;

            if (!condition)
            {
                problems.Add(message);
            }
        }

        try
        {
            Directory.CreateDirectory(scratch);

            ParserCases(scratch, Expect);
            FieldCases(scratch, Expect);
            ConsumedCases(scratch, Expect);
            UnconsumedCases(scratch, Expect);
            AnchorCases(scratch, Expect);
            TreeCases(scratch, Expect);
            RollbackCases(scratch, Expect);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        Console.WriteLine($"Self-test: {checks} check(s), {problems.Count} problem(s).");

        foreach (string problem in problems)
        {
            Console.Error.WriteLine($"  {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private const string _module = "Src/Application/A.Application/ApplicationModule.cs";

    private const string _list = "Tests/Architecture/A.Architecture.Tests/Rules/DomainEventTests.cs";

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
        Put("Src/Domain/A.Domain/Features/Invoices/Repositories/IInvoiceRepository.cs", "public interface IInvoiceRepository;\n");
        Put("Src/Domain/A.Domain/Features/Parcels/Repositories/IParcelRepository.cs", "public interface IParcelRepository;\n");
        Put("Src/Domain/A.Domain/Features/Parcels/Repositories/ICourierRepository.cs", "public interface ICourierRepository;\n");
        Put("Src/Domain/A.Domain/Features/Invoices/Model/Events/InvoiceIssuedDomainEvent.cs", "public sealed record InvoiceIssuedDomainEvent(Guid InvoiceId, DateTimeOffset OccurredOn) : IDomainEvent;\n");
        Directory.CreateDirectory(Path.Combine(root, "Src/Domain/A.Domain/Features/Notes/Model"));
        Directory.CreateDirectory(Path.Combine(root, "Src/Application/A.Application/Features/Reminders/Services"));
        Directory.CreateDirectory(Path.Combine(root, "Src/Application/A.Application/Features/Files/Services"));
        Directory.CreateDirectory(Path.Combine(root, "Tests/Domain/A.Domain.UnitTests"));
        Directory.CreateDirectory(Path.Combine(root, "Tests/Application/A.Application.UnitTests"));

        Put(
            _module,
            """
            using Acme.Application.Core;
            using Acme.Application.Core.Common.Events;
            using Acme.Application.Features.Reminders.Services;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Application;

            public static class ApplicationModule
            {
                public static IServiceCollection AddReminders(this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);

                    services.AddFeature("Reminders");
                    services.AddScoped<IReminderService, ReminderService>();

                    services.AddDomainEventConsumer<UserAccountDeleted, DeleteRemindersOnAccountDeletedConsumer>();

                    return services;
                }

                public static IServiceCollection AddFiles(this IServiceCollection services, IConfiguration configuration)
                {
                    ArgumentNullException.ThrowIfNull(services);
                    ArgumentNullException.ThrowIfNull(configuration);

                    services.AddFeature("Files");

                    return services;
                }
            }

            """);
        Put(_list, "public sealed class DomainEventTests\n{\n    private static readonly string[] _deliberatelyUnconsumed =\n    [\n        \"InvoiceIssuedDomainEvent\",\n    ];\n\n    [Fact]\n    public void Something()\n    {\n    }\n}\n");
        Put(".feature-manifest/Invoices.json", "{\n  \"feature\": \"Invoices\",\n  \"files\": []\n}\n", bom: false);

        return root;
    }

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Run([.. args, "--root", root], output, error);

        return (code, output.ToString(), error.ToString());
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
        string[] consumed = ["--consumer", "Reminders:CancelReminders"];

        (string[] Args, string Fragment)[] refusals =
        [
            (["Invoices", "InvoicePaidDomainEvent"], "Say who listens"),
            (["Invoices", "InvoicePaidDomainEvent", .. consumed, "--unconsumed", "Nobody reacts for now"], "exclude each other"),
            (["Invoices", "InvoicePaidDomainEvent", "--consumer"], "'--consumer' needs a value"),
            (["Invoices", "InvoicePaidDomainEvent", .. consumed, "--consumer", "Files:Do"], "is given twice"),
            (["Invoices", "InvoicePaidDomainEvent", .. consumed, "--force"], "'--force' is not an option"),
            (["Invoices", .. consumed], "Expected two names"),
            (["invoices", "InvoicePaidDomainEvent", .. consumed], "is not a feature folder"),
            (["Invoices", "invoicePaid", .. consumed], "is not an event name"),
            (["Invoices", "InvoicePaid", .. consumed], "does not end in DomainEvent"),
            (["Invoices", "DomainEvent", .. consumed], "does not end in DomainEvent after a name of its own"),
            (["Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders"], "is not written <Features>:<Reaction>"),
            (["Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:cancel"], "is not written <Features>:<Reaction>"),
            (["Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelConsumer"], "without 'Consumer'"),
            (["Invoices", "InvoicePaidDomainEvent", "--consumer", "Gadgets:Cancel"], "No feature called Gadgets"),
            (["Invoices", "InvoicePaidDomainEvent", "--unconsumed", "no"], "one line of 8 to 120"),
            (["Invoices", "InvoicePaidDomainEvent", "--unconsumed", "no one reacts // yet"], "no '//' in it"),
            (["Invoices", "InvoicePaidDomainEvent", "--unconsumed", "no one reacts\nyet"], "one line of 8 to 120"),
            (["Invoices", "InvoiceIssuedDomainEvent", .. consumed], "already exists"),
            (["Memos", "MemoPinnedDomainEvent", .. consumed, "--fields", "MemoId:Guid"], "service over a store"),
            (["Parcels", "ParcelSentDomainEvent", .. consumed], "--fields is needed"),
            (["Invoices", new string('A', 61) + "DomainEvent", .. consumed], "longer than 60"),
        ];

        foreach (var (args, fragment) in refusals)
        {
            ExpectRefusal(root, args, fragment, expect);
        }

        var several = Invoke(root, "invoices", "paid", "--fields", "x:nope, ,Y");

        expect(several.Error.Split('\n').Count(line => line.StartsWith("error:", StringComparison.Ordinal)) >= 3, "Several mistakes in one command were not all reported.");
    }

    private static void FieldCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string[] unconsumed = ["--unconsumed", "Nothing reacts to it yet"];

        (string Fields, string Fragment)[] refused =
        [
            ("InvoiceId", "is not written Name:Type"),
            ("InvoiceId:Guid:x", "is not written Name:Type"),
            ("invoiceId:Guid", "is not a field name"),
            ("OccurredOn:DateTimeOffset", "declares itself"),
            ("Class:string", "C# keyword once lowered"),
            ("InvoiceId:Guid, invoiceid:int", "is not a field name"),
            ("InvoiceId:Guid, InvoiceID:int", "more than once"),
            ("Items:List<string>", "is not a type of this tool"),
            ("Amount:double", "is not a type of this tool"),
            ("Reason:string?", "is not a type of this tool"),
            ("InvoiceId:Guid,", "is not written Name:Type"),
        ];

        foreach (var (fields, fragment) in refused)
        {
            ExpectRefusal(root, ["Invoices", "InvoicePaidDomainEvent", .. unconsumed, "--fields", fields], fragment, expect);
        }

        string many = string.Join(", ", Enumerable.Range(0, 13).Select(index => $"Field{(char)('A' + index)}:int"));

        ExpectRefusal(root, ["Invoices", "InvoicePaidDomainEvent", .. unconsumed, "--fields", many], "at most 12 fields", expect);
    }

    private static void ConsumedCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);
        var dry = Invoke(root, "Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders", "--dry-run");

        expect(dry.Code == 0 && dry.Output.Contains("Dry run: nothing was written.", StringComparison.Ordinal), $"The dry run failed: {dry.Error}");
        expect(Same(before, Snapshot(root)), "The dry run changed the tree.");

        var result = Invoke(root, "Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders", "--fields", "InvoiceId:Guid, Reason:string, Amount:decimal, PaidOn:DateOnly, Late:bool, Lines:int, Cents:long, At:DateTimeOffset");

        expect(result.Code == 0, $"A valid event was refused: {result.Error}");

        string record = "Src/Domain/A.Domain/Features/Invoices/Model/Events/InvoicePaidDomainEvent.cs";
        string eventTests = "Tests/Domain/A.Domain.UnitTests/Features/Invoices/Model/Events/InvoicePaidDomainEventTests.cs";
        string consumer = "Src/Application/A.Application/Features/Reminders/Services/Consumers/InvoicePaid/CancelRemindersOnInvoicePaidConsumer.cs";
        string consumerTests = "Tests/Application/A.Application.UnitTests/Features/Reminders/Services/Consumers/InvoicePaid/CancelRemindersOnInvoicePaidConsumerTests.cs";

        void Holds(string text, string fragment, string what) =>
            expect(text.ReplaceLineEndings("\n").Contains(fragment, StringComparison.Ordinal), $"{what} does not hold '{fragment}'.");

        Holds(
            Read(root, record),
            "public sealed record InvoicePaidDomainEvent(\n    Guid InvoiceId,\n    string Reason,\n    decimal Amount,\n    DateOnly PaidOn,\n    bool Late,\n    int Lines,\n    long Cents,\n    DateTimeOffset At,\n    DateTimeOffset OccurredOn) : IDomainEvent;",
            "The event");
        Holds(Read(root, record), "namespace Acme.Domain.Features.Invoices.Model.Events;", "The event");
        Holds(Read(root, eventTests), "var guid0 = Guid.CreateVersion7();", "The event's tests");
        Holds(Read(root, eventTests), "new InvoicePaidDomainEvent(guid0, \"reason\", 1m, new DateOnly(2026, 1, 1), true, 1, 1L, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), _occurredOn);", "The event's tests");
        Holds(Read(root, eventTests), "domainEvent.InvoiceId.ShouldBe(guid0);", "The event's tests");
        Holds(Read(root, eventTests), "typeof(IDomainEvent).IsAssignableFrom(typeof(InvoicePaidDomainEvent)).ShouldBeTrue();", "The event's tests");
        Holds(Read(root, consumer), "internal sealed class CancelRemindersOnInvoicePaidConsumer : IDomainEventConsumer<InvoicePaidDomainEvent>", "The consumer");
        Holds(Read(root, consumer), "namespace Acme.Application.Features.Reminders.Services.Consumers.InvoicePaid;", "The consumer");
        Holds(Read(root, consumer), "ArgumentNullException.ThrowIfNull(domainEvent);", "The consumer");
        Holds(Read(root, consumerTests), "services.AddReminders();", "The consumer's tests");
        Holds(Read(root, consumerTests), "public void TheReaction_IsWritten() =>\n        Assert.Fail(", "The consumer's tests");
        Holds(Read(root, consumerTests), "CancelReminders is not written:", "The consumer's tests");
        expect(!Read(root, consumerTests).Contains("ThrowAsync<NotImplementedException>", StringComparison.Ordinal), "The consumer's tests assert that the unwritten reaction throws, which keeps the gate green.");
        Holds(Read(root, consumerTests), "descriptor.ImplementationType == typeof(CancelRemindersOnInvoicePaidConsumer)", "The consumer's tests");

        string module = Read(root, _module);

        Holds(module, "using Acme.Application.Features.Reminders.Services.Consumers.InvoicePaid;\n", "The module's usings");
        Holds(module, "using Acme.Domain.Features.Invoices.Model.Events;\n", "The module's usings");
        Holds(
            module,
            "DeleteRemindersOnAccountDeletedConsumer>();\n\n        services.AddDomainEventConsumer<InvoicePaidDomainEvent, CancelRemindersOnInvoicePaidConsumer>();\n\n        return services;",
            "The registration");
        expect(Read(root, _list).Contains("InvoicePaid", StringComparison.Ordinal) is false, "A consumed event was added to the unconsumed list.");

        foreach (string file in new[] { record, eventTests, consumer, consumerTests })
        {
            foreach (string finding in FileProblems(root, file))
            {
                expect(false, finding);
            }
        }

        string manifest = Read(root, ".feature-manifest/Invoices.json");

        Holds(manifest, record, "The feature's manifest");
        Holds(manifest, consumer, "The feature's manifest");

        var again = Snapshot(root);

        ExpectRefusal(root, ["Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders"], "already exists", expect);
        expect(Same(again, Snapshot(root)), "A second run changed the tree.");

        var configured = Invoke(root, "Invoices", "InvoiceVoidedDomainEvent", "--consumer", "Files:ReclaimFiles");

        expect(configured.Code == 0, $"A consumer in a feature taking the configuration was refused: {configured.Error}");
        Holds(
            Read(root, "Tests/Application/A.Application.UnitTests/Features/Files/Services/Consumers/InvoiceVoided/ReclaimFilesOnInvoiceVoidedConsumerTests.cs"),
            "services.AddFiles(new ConfigurationBuilder().Build());",
            "The tests of a consumer in a feature that takes the configuration");
        Holds(Read(root, record.Replace("InvoicePaid", "InvoiceVoided", StringComparison.Ordinal)), "(\n    Guid InvoiceId,\n    DateTimeOffset OccurredOn)", "The event with its default field");
    }

    private static void UnconsumedCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var dry = Snapshot(root);
        var result = Invoke(root, "Invoices", "InvoiceNotedDomainEvent", "--unconsumed", "Nothing reacts to a note", "--fields", "InvoiceId:Guid");

        expect(result.Code == 0, $"An event with no consumer was refused: {result.Error}");
        expect(!Same(dry, Snapshot(root)), "An unconsumed event wrote nothing.");

        string list = Read(root, _list);

        expect(
            list.Contains("        \"InvoiceIssuedDomainEvent\",\n        // Nothing reacts to a note\n        \"InvoiceNotedDomainEvent\",\n    ];", StringComparison.Ordinal),
            "The event and its reason did not join the closed list.");
        expect(!Directory.Exists(Path.Combine(root, "Src/Application/A.Application/Features/Reminders/Services/Consumers")), "An unconsumed event wrote a consumer.");
        expect(Read(root, _module).Contains("InvoiceNoted", StringComparison.Ordinal) is false, "An unconsumed event was registered.");
        expect(result.Output.Contains("delete that entry", StringComparison.Ordinal), "The report did not say what to do once a consumer is written.");

        ExpectRefusal(root, ["Invoices", "InvoiceNotedDomainEvent", "--unconsumed", "Nothing reacts to a note", "--fields", "InvoiceId:Guid"], "already exists", expect);
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

        if (!Regex.IsMatch(text, @"^namespace Acme\.(?:Domain|Application)(?:\.UnitTests)?\.Features\.\w+\.", RegexOptions.Multiline, TimeSpan.FromSeconds(1)))
        {
            yield return $"{relative}: its namespace is not its project plus its folders.";
        }
    }

    private static void AnchorCases(string scratch, Action<bool, string> expect)
    {
        string[] consumed = ["Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders"];
        string[] unconsumed = ["Invoices", "InvoicePaidDomainEvent", "--unconsumed", "Nothing reacts to it yet"];

        (string Name, string[] Args, string File, Func<string, string> Damage, string Fragment)[] cases =
        [
            ("no method", consumed, _module, text => text.Replace("AddReminders", "AddAlerts", StringComparison.Ordinal), "the registration in AddReminders"),
            ("no return", consumed, _module, text => text.Replace("        return services;\n    }\n\n    public static IServiceCollection AddFiles", "        return;\n    }\n\n    public static IServiceCollection AddFiles", StringComparison.Ordinal), "the registration in AddReminders"),
            ("no blank before return", consumed, _module, text => text.Replace("DeleteRemindersOnAccountDeletedConsumer>();\n\n        return", "DeleteRemindersOnAccountDeletedConsumer>();\n        return", StringComparison.Ordinal), "the registration in AddReminders"),
            ("usings out of order", consumed, _module, text => text.Replace("using Acme.Application.Core;\nusing Acme.Application.Core.Common.Events;", "using Acme.Application.Core.Common.Events;\nusing Acme.Application.Core;", StringComparison.Ordinal), "the usings of"),
            ("no list", unconsumed, _list, text => text.Replace("_deliberatelyUnconsumed", "_excused", StringComparison.Ordinal), "the _deliberatelyUnconsumed list of"),
            ("no list end", unconsumed, _list, text => text.Replace("\n    ];\n", "\n    ] ;\n", StringComparison.Ordinal), "the _deliberatelyUnconsumed list of"),
            ("already listed", unconsumed, _list, text => text.Replace("InvoiceIssuedDomainEvent", "InvoicePaidDomainEvent", StringComparison.Ordinal), "the _deliberatelyUnconsumed list of"),
            ("crlf", consumed, _module, text => text.ReplaceLineEndings("\r\n"), "CRLF line endings"),
        ];

        foreach (var (name, args, file, damage, fragment) in cases)
        {
            string root = NewTree(scratch);
            string full = Path.Combine(root, file);
            byte[] bytes = File.ReadAllBytes(full);

            File.WriteAllBytes(full, [.. bytes is [0xEF, 0xBB, 0xBF, ..] ? Encoding.UTF8.GetPreamble() : [], .. Encoding.UTF8.GetBytes(damage(Decode(bytes)))]);
            ExpectRefusal(root, args, fragment, expect);
        }
    }

    private static void TreeCases(string scratch, Action<bool, string> expect)
    {
        string stranger = Path.Combine(scratch, "stranger");
        Directory.CreateDirectory(stranger);

        var elsewhere = Invoke(stranger, "Invoices", "InvoicePaidDomainEvent", "--unconsumed", "Nothing reacts to it yet");

        expect(elsewhere.Code == 1 && elsewhere.Error.Contains("No cleanarch.json", StringComparison.Ordinal), "A tree without a project file was not refused for it.");
        expect(!Directory.EnumerateFileSystemEntries(stranger).Any(), "A refused tree was written to.");

        string template = NewTree(scratch);
        string module = Path.Combine(template, _module);

        File.WriteAllText(module, "//" + _conditionOpen + "files)\n" + File.ReadAllText(module).Replace("\uFEFF", string.Empty, StringComparison.Ordinal) + "//" + _conditionClose + "\n");
        ExpectRefusal(template, ["Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders"], "template's own tree", expect);

        string noList = NewTree(scratch);

        File.Delete(Path.Combine(noList, _list));
        ExpectRefusal(noList, ["Invoices", "InvoicePaidDomainEvent", "--unconsumed", "Nothing reacts to it yet"], "closed list of events", expect);

        string untested = NewTree(scratch);

        Directory.Delete(Path.Combine(untested, "Tests/Domain"), recursive: true);
        Directory.Delete(Path.Combine(untested, "Tests/Application"), recursive: true);

        var result = Invoke(untested, "Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders");

        expect(result.Code == 0, $"A tree without unit test projects was refused: {result.Error}");
        expect(!Directory.Exists(Path.Combine(untested, "Tests/Domain")) && !Directory.Exists(Path.Combine(untested, "Tests/Application")), "Tests were written where no test project exists.");
    }

    private static void RollbackCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        Directory.CreateDirectory(Path.Combine(root, _module + ".new"));

        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), $"A write that failed midway did not say so: {result.Error}");
        expect(Same(before, Snapshot(root)), "A write that failed midway left files or edits behind.");
    }
}
