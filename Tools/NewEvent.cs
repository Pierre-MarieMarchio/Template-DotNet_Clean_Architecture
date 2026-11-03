#:property UseAppHost=false

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class NewEvent
{
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
        + "  --fields      what the event carries besides OccurredOn, which it always ends with. Name:type, in the\n"
        + "                grammar of new-feature --fields, a type of Guid, string, int, long, bool, decimal,\n"
        + "                DateOnly or DateTimeOffset, with no length, precision, bound or '?': no column holds\n"
        + "                an event, and the aggregate checks a value before it raises the event with it. By\n"
        + "                default the id of the feature's aggregate, when it has exactly one repository.\n"
        + "  --dry-run     print what would be written, and write nothing.\n"
        + "  --root        the tree to write into, default the current directory.\n"
        + "       dotnet run Tools/NewEvent.cs --self-test";

    private static readonly string[] _optionNames = ["--consumer", "--unconsumed", "--fields", "--root", "--dry-run"];

    private sealed record Emission(string Path, string Content);

    private sealed record Request(string? Features, string? Name, string? Consumer, string? Unconsumed, string? Fields, string Root, bool DryRun);

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

        var editor = Edits(spec, root, problems);

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        problems.AddRange(editor.Failures.Select(failure => $"{failure} Fix that file, or make this edit by hand."));

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        var replaced = TextEdits.Replacements(editor).ToList();

        if (spec.Manifest is not null)
        {
            try
            {
                replaced.Add(ManifestWith(root, spec.Features, emissions, editor));
            }
            catch (InvalidOperationException exception)
            {
                problems.Add(
                    $"{exception.Message} {spec.Manifest} records each edit for remove-feature to undo with the feature: "
                    + "give the file the ending this project writes (format-fix), or make this edit by hand, then run this again.");

                return Refuse(error, problems);
            }
        }

        if (!request.DryRun)
        {
            try
            {
                TextEdits.Commit(root, [.. emissions.Select(emission => (emission.Path, TextEdits.Encode(emission.Path, emission.Content)))], replaced);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

                return 1;
            }
        }

        Report(output, spec, emissions, editor.Changed.Select(sheet => sheet.Path), request.DryRun);

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

        if (!CSharpNames.PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!CSharpNames.PascalCase.IsMatch(name))
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

            if (parts.Length != 2 || !CSharpNames.PascalCase.IsMatch(parts[0]) || !CSharpNames.PascalCase.IsMatch(parts[1]))
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

        var manifest = FeatureManifest.Read(root, features, out string? unread);

        if (unread is not null)
        {
            problems.Add($"'{FeatureManifest.PathOf(features)}' {unread}, so the files written could not be recorded in it. Repair or delete it first.");

            return null;
        }

        return new Spec(prefix, features, name, fields, consumerFeatures, reaction, takes, request.Unconsumed?.Trim(), manifest?.Path);
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
                    .Where(type => CSharpNames.PascalCase.IsMatch(type))
                    .Order(StringComparer.Ordinal)]
                : [];

            if (found.Length != 1)
            {
                problems.Add(
                    $"--fields is needed: {features} declares {(found.Length == 0 ? "no repository" : "several repositories, " + string.Join(", ", found))} under Repositories/, so there is no single aggregate whose id the event carries by default. "
                    + "Pass --fields \"InvoiceId:Guid\".");

                return fields;
            }

            return [new Field(found[0] + "Id", FieldKind.Guid, Nullable: false, Length: 0, Precision: 0, Scale: 0, Lower: null, Upper: null, Position: 0)];
        }

        if (FieldGrammar.Parse(text, FieldProfile.Event, problems) is not { } parsed)
        {
            return fields;
        }

        fields.AddRange(parsed);

        if (fields.FirstOrDefault(field => field.Name == name) is { } own)
        {
            problems.Add($"--fields, at character {own.Position + 1}: {name} is the event's own name, which a property of it cannot share. Choose another name.");
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
        string parameters = string.Concat(spec.Fields.Select(field => $"    {field.ClrType} {field.Name},\n"));

        return Sorted(
            $"using {spec.Prefix}.Domain.Core.Common.Events;\n\n"
            + $"namespace {spec.EventNamespace};\n\n"
            + $"public sealed record {spec.Name}(\n{parameters}    DateTimeOffset OccurredOn) : IDomainEvent;\n");
    }

    private static string Sample(Field field, int index) => field.Kind switch
    {
        FieldKind.Guid => $"guid{index}",
        FieldKind.String => $"\"{Words(field.Name)}\"",
        FieldKind.Int => "1",
        FieldKind.Long => "1L",
        FieldKind.Bool => "true",
        FieldKind.Decimal => "1m",
        FieldKind.DateOnly => "new DateOnly(2026, 1, 1)",
        _ => "new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)",
    };

    private static string Words(string pascal) =>
        string.Concat(pascal.Select((character, index) => char.IsUpper(character) && index > 0 ? " " + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));

    private static string GuidDeclarations(Spec spec) =>
        string.Concat(spec.Fields.Select((field, index) => (field, index)).Where(pair => pair.field.Kind == FieldKind.Guid).Select(pair => $"        var guid{pair.index} = Guid.CreateVersion7();\n"));

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

    private static TextEdits.Editor Edits(Spec spec, string root, List<string> problems)
    {
        var editor = new TextEdits.Editor(root);
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

            editor.Step = $"the registration in Add{spec.ConsumerFeatures} of {module}";
            editor.Rewrite(module, text => Register(text, spec));
            editor.Step = $"the usings of {module}";

            foreach (string space in new[] { $"{spec.Prefix}.Application.Core", spec.EventNamespace, spec.ConsumerNamespace })
            {
                editor.Using(module, space);
            }
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
                editor.Step = $"the _deliberatelyUnconsumed list of {list}";
                editor.Rewrite(list, text => Excuse(text, spec));
            }
        }

        return editor;
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

    private static string Decode(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes, bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0, bytes.Length - (bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0));

    private static (string Path, byte[] Original, byte[] Bytes) ManifestWith(string root, string features, List<Emission> plan, TextEdits.Editor editor)
    {
        var manifest = FeatureManifest.Read(root, features, out _)!;

        foreach (var emission in plan)
        {
            manifest.Record(emission.Path, TextEdits.Encode(emission.Path, emission.Content));
        }

        manifest.Record(editor);

        return manifest.Replacement;
    }

    private static string Sorted(string source)
    {
        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        int usings = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        string[] sorted = [.. lines[..usings].Distinct(StringComparer.Ordinal).OrderBy(UsingKey, StringComparer.Ordinal)];

        return string.Join('\n', [.. sorted, .. lines[usings..]]).TrimEnd('\n') + "\n";
    }

    private static string UsingKey(string line) => (line.StartsWith("using System", StringComparison.Ordinal) ? "0" : "1") + line.TrimEnd(';').ToUpperInvariant();

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
            ("InvoiceId", "expected ':' after the field name 'InvoiceId'"),
            ("InvoiceId:Guid:x", "expected a comma before the next field"),
            ("invoiceId:Guid", "is not a field name"),
            ("OccurredOn:DateTimeOffset", "a member the event declares itself"),
            ("ToString:string", "a member the event declares itself"),
            ("InvoicePaidDomainEvent:Guid", "the event's own name"),
            ("Class:string", "becomes 'class' once its first letter is lowered"),
            ("InvoiceId:Guid, invoiceid:int", "is not a field name"),
            ("InvoiceId:Guid, InvoiceID:int", "the same name but for its case"),
            ("Items:List<string>", "expected a type, one of Guid, string, int, long, bool, decimal, DateOnly, DateTimeOffset"),
            ("Amount:double", "double is not a field type: it is not exact, and the providers store it differently. Use decimal."),
            ("Rate:float", "Use decimal."),
            ("Initial:char", "char is not a field type. Use string."),
            ("Total:ulong", "Use long or decimal."),
            ("Kind:enum", "Carry a string, whose values the aggregate checks."),
            ("At:DateTime", "Use DateTimeOffset"),
            ("Notes:text", "text is not a type of an event"),
            ("Reason:string(40)", "takes no length or precision"),
            ("Amount:decimal(9,2)", "takes no length or precision"),
            ("Reason:string?", "takes no missing value"),
            ("Amount:decimal>=0", "takes no bound"),
            ("InvoiceId:Guid,", "expected a field name"),
            ("", "--fields is empty"),
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
        RecordedEditsUndo(root, before, expect);

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
        RecordedEditsUndo(root, dry, expect);

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

        string unsorted = NewTree(scratch);
        string module = Path.Combine(unsorted, _module);

        File.WriteAllBytes(
            module,
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Decode(File.ReadAllBytes(module)).Replace("using Acme.Application.Core;\nusing Acme.Application.Core.Common.Events;", "using Acme.Application.Core.Common.Events;\nusing Acme.Application.Core;", StringComparison.Ordinal))]);

        var result = Invoke(unsorted, consumed);
        string[] lines = Read(unsorted, _module).Split('\n');
        string[] usings = ["using Acme.Application.Core;", "using Acme.Domain.Features.Invoices.Model.Events;", "using Acme.Application.Features.Reminders.Services.Consumers.InvoicePaid;"];

        expect(
            result.Code == 0 && usings.All(line => lines.Count(candidate => candidate == line) == 1),
            $"A module whose usings are out of order did not get the consumer's usings, each once: {result.Error}");
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

        File.WriteAllText(Path.Combine(root, "Tests/Domain/A.Domain.UnitTests/Features"), "not a folder\n");

        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "InvoicePaidDomainEvent", "--consumer", "Reminders:CancelReminders");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), $"A write that failed midway did not say so: {result.Error}");
        expect(Same(before, Snapshot(root)), "A write that failed midway left files or edits behind.");

        string edited = NewTree(scratch);
        var untouched = Snapshot(edited);
        bool restored = false;

        try
        {
            TextEdits.Commit(edited, [("Src/written.cs", [1])], [(_module, File.ReadAllBytes(Path.Combine(edited, _module)), [2]), ("Src", [], [3])]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            restored = Same(untouched, Snapshot(edited));
        }

        expect(restored, "An edit that could not be written did not put back the files edited before it and remove the files written.");
    }
}

internal static class CSharpNames
{
    internal static readonly string[] Keywords =
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

    internal static readonly Regex PascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    internal static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}

internal enum FieldKind
{
    String,
    Text,
    Int,
    Long,
    Decimal,
    Bool,
    DateOnly,
    DateTimeOffset,
    Guid,
}

internal sealed record FieldBound(string Operator, decimal Value, string Literal)
{
    internal bool IsLower => Operator[0] == '>';

    internal bool IsInclusive => Operator.Length == 2;

    internal string Suffix => (IsInclusive ? string.Empty : "Exclusive") + (IsLower ? "Minimum" : "Maximum");

    internal string Wording => Operator switch
    {
        ">=" => "at least",
        ">" => "greater than",
        "<=" => "at most",
        _ => "less than",
    };

    internal bool Holds(decimal value) => Operator switch
    {
        ">=" => value >= Value,
        ">" => value > Value,
        "<=" => value <= Value,
        _ => value < Value,
    };
}

internal sealed record Field(
    string Name,
    FieldKind Kind,
    bool Nullable,
    int Length,
    int Precision,
    int Scale,
    FieldBound? Lower,
    FieldBound? Upper,
    int Position)
{
    internal IEnumerable<FieldBound> Bounds => new[] { Lower, Upper }.OfType<FieldBound>();

    internal string TypeText => Kind switch
    {
        FieldKind.String => Length == 0 ? "string" : $"string({Length})",
        FieldKind.Text => $"text({Length})",
        FieldKind.Decimal => Precision == 0 ? "decimal" : $"decimal({Precision},{Scale})",
        FieldKind.Int => "int",
        FieldKind.Long => "long",
        FieldKind.Bool => "bool",
        FieldKind.DateOnly => "DateOnly",
        FieldKind.DateTimeOffset => "DateTimeOffset",
        _ => "Guid",
    };

    internal string ClrType => Kind switch
    {
        FieldKind.String or FieldKind.Text => "string",
        FieldKind.Int => "int",
        FieldKind.Long => "long",
        FieldKind.Decimal => "decimal",
        FieldKind.Bool => "bool",
        FieldKind.DateOnly => "DateOnly",
        FieldKind.DateTimeOffset => "DateTimeOffset",
        _ => "Guid",
    };

    internal string Declaration =>
        $"{Name}:{TypeText}{(Nullable ? "?" : string.Empty)}{string.Concat(Bounds.Select(bound => bound.Operator + bound.Literal))}";

    internal FieldDefault? Default { get; init; }

    internal string Declared => Default is { } value ? $"{Declaration}={value.Literal}" : Declaration;
}

internal sealed record FieldDefault(string Literal, string Expression);

internal sealed record FieldProfile(
    string Holder,
    string Types,
    string Example,
    FieldKind[] Kinds,
    bool Stored,
    string[] ReservedMembers,
    string ReservedWording,
    bool ReservesGeneratedNames,
    string DuplicateReason,
    string UnstoredReason)
{
    internal bool TakesDefault { get; init; }

    internal static FieldProfile Feature { get; } = new(
        "a feature",
        "string(n), text, text(n), int, long, decimal, decimal(p,s), bool, DateOnly, DateTimeOffset, Guid",
        "--fields \"Title:string(120), Budget:decimal>=0\"",
        Enum.GetValues<FieldKind>(),
        Stored: true,
        [
            "Id", "OwnerId", "Version", "CreatedAt", "CreatedBy", "LastModifiedAt", "LastModifiedBy", "Precondition",
            "AnonymisedText", "DomainEvents", "ClearDomainEvents", "RaiseDomainEvent", "Create", "Rehydrate", "Update",
            "Anonymise", "EqualityContract", "Deconstruct", "PrintMembers", "Equals", "GetHashCode", "GetType", "ToString",
            "MemberwiseClone", "Finalize",
        ],
        "a column or a member the feature already declares (the id, the owner, the version, the audit stamps, or a method "
            + "of the aggregate or of a record)",
        ReservesGeneratedNames: true,
        "two columns a case-insensitive database cannot tell apart",
        string.Empty);

    internal static FieldProfile Event { get; } = new(
        "an event",
        "Guid, string, int, long, bool, decimal, DateOnly, DateTimeOffset",
        "--fields \"InvoiceId:Guid, Amount:decimal\"",
        [FieldKind.Guid, FieldKind.String, FieldKind.Int, FieldKind.Long, FieldKind.Bool, FieldKind.Decimal, FieldKind.DateOnly, FieldKind.DateTimeOffset],
        Stored: false,
        [
            "OccurredOn", "EqualityContract", "Deconstruct", "PrintMembers", "Equals", "GetHashCode", "GetType", "ToString",
            "MemberwiseClone", "Finalize",
        ],
        "a member the event declares itself (OccurredOn, or a method of a record)",
        ReservesGeneratedNames: false,
        "two properties a case-insensitive JSON reader cannot tell apart",
        "No column holds an event's fields, and the aggregate checks a value before it raises the event with it: a length, a "
            + "precision, a bound or a missing value is the aggregate's rule, not the event's");

    internal static FieldProfile AddedField { get; } = Feature with { TakesDefault = true };
}

internal static class FieldGrammar
{
    internal const int StringMaxLength = 400;

    internal const int TextDefaultLength = 2_000;

    internal const int TextMaxLength = 65_536;

    internal const int DecimalDefaultPrecision = 18;

    internal const int DecimalDefaultScale = 2;

    internal const int DecimalMaxPrecision = 28;

    internal const int NameMaxLength = 40;

    private static readonly string[] _reservedTypes =
    [
        "Guid", "DateTimeOffset", "DateOnly", "TimeSpan", "String", "Decimal", "Math", "DomainException",
        "ArgumentNullException", "UserId", "JsonRequired",
    ];

    private static readonly string[] _generatedSuffixes =
        ["MaxLength", "Precision", "Scale", "Minimum", "ExclusiveMinimum", "Maximum", "ExclusiveMaximum", "Field"];

    private static readonly string[] _generatedPrefixes = ["Require", "Valid"];

    private static readonly string[] _contextualKeywords = ["await", "nameof", "var", "value"];

    private static readonly Dictionary<string, (string Reason, string Stored, string Unstored)> _refusedTypes = new(StringComparer.Ordinal)
    {
        ["enum"] = (
            "enum is not a field type: an enum's values are a decision of the domain, and a stored enum changes "
                + "meaning when a member is reordered or renamed",
            "Declare a string(n) and check its values by hand, or write a value object",
            "Carry a string, whose values the aggregate checks"),
        ["DateTime"] = ("DateTime is not a field type: it carries no offset, so the instant it names depends on the server", "Use DateTimeOffset", "Use DateTimeOffset"),
        ["double"] = ("double is not a field type: it is not exact, and the providers store it differently", "Use decimal or decimal(p,s)", "Use decimal"),
        ["float"] = ("float is not a field type: it is not exact, and the providers store it differently", "Use decimal or decimal(p,s)", "Use decimal"),
        ["short"] = ("short is not a field type", "Use int, with bounds if the range matters", "Use int"),
        ["byte"] = ("byte is not a field type", "Use int, with bounds if the range matters", "Use int"),
        ["uint"] = ("uint is not a field type: no provider stores it alike", "Use long, with >=0", "Use long"),
        ["ulong"] = ("ulong is not a field type: no provider stores it alike", "Use long, with >=0, or decimal(20,0)", "Use long or decimal"),
        ["char"] = ("char is not a field type", "Use string(1)", "Use string"),
    };

    private static readonly Dictionary<string, FieldKind> _kinds = new(StringComparer.Ordinal)
    {
        ["string"] = FieldKind.String,
        ["text"] = FieldKind.Text,
        ["int"] = FieldKind.Int,
        ["long"] = FieldKind.Long,
        ["decimal"] = FieldKind.Decimal,
        ["bool"] = FieldKind.Bool,
        ["DateOnly"] = FieldKind.DateOnly,
        ["DateTimeOffset"] = FieldKind.DateTimeOffset,
        ["Guid"] = FieldKind.Guid,
    };

    private enum TokenKind
    {
        Name,
        Number,
        Symbol,
        Quoted,
        Unknown,
        End,
    }

    private sealed record Token(TokenKind Kind, string Text, int Position);

    internal static List<Field>? Parse(string text, FieldProfile profile, List<string> problems)
    {
        int before = problems.Count;
        var tokens = Tokenize(text);

        if (tokens.Count == 1)
        {
            problems.Add($"--fields is empty. It lists the fields, Name:type, comma separated, as in {profile.Example}.");

            return null;
        }

        var fields = new List<Field>();
        int at = 0;

        while (true)
        {
            if (ParseField(tokens, ref at, profile, problems) is not { } field)
            {
                return null;
            }

            fields.Add(field);

            if (tokens[at].Kind == TokenKind.End)
            {
                break;
            }

            if (tokens[at].Text != ",")
            {
                problems.Add(Expected(tokens[at], "a comma before the next field, or the end of the list"));

                return null;
            }

            at++;
        }

        problems.AddRange(NameProblems(fields, profile));

        return problems.Count == before ? fields : null;
    }

    private static Field? ParseField(List<Token> tokens, ref int at, FieldProfile profile, List<string> problems)
    {
        var name = tokens[at];

        if (name.Kind != TokenKind.Name)
        {
            problems.Add(Expected(name, "a field name, PascalCase, as Title"));

            return null;
        }

        at++;

        if (tokens[at].Text != ":")
        {
            problems.Add(Expected(tokens[at], $"':' after the field name {Shown(name.Text)}, then its type"));

            return null;
        }

        at++;

        var typeToken = tokens[at];

        if (typeToken.Kind != TokenKind.Name)
        {
            problems.Add(Expected(typeToken, $"a type, one of {profile.Types}"));

            return null;
        }

        if (_kinds.TryGetValue(typeToken.Text, out FieldKind named) && !profile.Kinds.Contains(named))
        {
            problems.Add($"--fields, at character {typeToken.Position + 1}: {typeToken.Text} is not a type of {profile.Holder}, which carries {profile.Types}.");

            return null;
        }

        at++;

        if (!profile.Stored && tokens[at].Text == "(")
        {
            problems.Add(Unstored(tokens[at], profile, "length or precision"));

            return null;
        }

        int length = 0, precision = 0, scale = 0;
        FieldKind kind;

        switch (typeToken.Text)
        {
            case "string" when !profile.Stored:
                kind = FieldKind.String;
                break;

            case "string":
                if (tokens[at].Text != "(")
                {
                    problems.Add(
                        $"--fields, at character {tokens[at].Position + 1}: string needs its maximum length, as string(120), "
                        + "so that no column is unbounded; a long free text is text or text(n).");

                    return null;
                }

                if (Arguments(tokens, ref at, 1, problems) is not [var maximum])
                {
                    return null;
                }

                if (maximum < 1 || maximum > StringMaxLength)
                {
                    problems.Add(
                        $"--fields: {name.Text} is string({maximum}), and a string is 1 to {StringMaxLength} characters long: "
                        + $"{StringMaxLength} keeps every string column indexable, and so sortable, on the three providers "
                        + "(SQL Server's 1,700-byte index key holds an nvarchar(400) beside the owner and the id). A longer value is text(n).");
                }

                kind = FieldKind.String;
                length = maximum;
                break;

            case "text":
                length = TextDefaultLength;

                if (tokens[at].Text == "(")
                {
                    if (Arguments(tokens, ref at, 1, problems) is not [var limit])
                    {
                        return null;
                    }

                    length = limit;
                }

                if (length < 1 || length > TextMaxLength)
                {
                    problems.Add(
                        $"--fields: {name.Text} is text({length}), and a text is 1 to {TextMaxLength:N0} characters long: the API "
                        + $"refuses a body over RequestLimits:MaxRequestBodyBytes, {TextMaxLength:N0} bytes by default, so a longer "
                        + "field could not arrive in a request. A document is a file.");
                }

                kind = FieldKind.Text;
                break;

            case "decimal" when !profile.Stored:
                kind = FieldKind.Decimal;
                break;

            case "decimal":
                precision = DecimalDefaultPrecision;
                scale = DecimalDefaultScale;

                if (tokens[at].Text == "(")
                {
                    if (Arguments(tokens, ref at, 2, problems) is not [var digits, var decimals])
                    {
                        return null;
                    }

                    precision = digits;
                    scale = decimals;
                }

                if (precision < 1 || precision > DecimalMaxPrecision || scale < 0 || scale > precision)
                {
                    problems.Add(
                        $"--fields: {name.Text} is decimal({precision},{scale}), and a decimal(p,s) has 1 to {DecimalMaxPrecision} "
                        + $"digits in all, the most a .NET decimal holds exactly, of which 0 to p after the point.");
                }

                kind = FieldKind.Decimal;
                break;

            case "int":
                kind = FieldKind.Int;
                break;

            case "long":
                kind = FieldKind.Long;
                break;

            case "bool":
                kind = FieldKind.Bool;
                break;

            case "DateOnly":
                kind = FieldKind.DateOnly;
                break;

            case "DateTimeOffset":
                kind = FieldKind.DateTimeOffset;
                break;

            case "Guid":
                kind = FieldKind.Guid;
                break;

            default:
                problems.Add(_refusedTypes.TryGetValue(typeToken.Text, out var refusal)
                    ? $"--fields, at character {typeToken.Position + 1}: {refusal.Reason}. {(profile.Stored ? refusal.Stored : refusal.Unstored)}."
                    : Expected(typeToken, $"a type, one of {profile.Types}, written as listed, case included"));

                return null;
        }

        bool nullable = false;

        if (tokens[at].Text == "?" && !profile.Stored)
        {
            problems.Add(Unstored(tokens[at], profile, "missing value"));

            return null;
        }

        if (tokens[at].Text == "?")
        {
            nullable = true;
            at++;
        }

        FieldBound? lower = null, upper = null;

        while (tokens[at].Text is ">=" or "<=" or ">" or "<")
        {
            var operation = tokens[at];

            if (!profile.Stored)
            {
                problems.Add(Unstored(operation, profile, "bound"));

                return null;
            }

            if (kind is not (FieldKind.Int or FieldKind.Long or FieldKind.Decimal))
            {
                problems.Add(
                    $"--fields, at character {operation.Position + 1}: {name.Text} is {typeToken.Text}, and only int, long and decimal "
                    + "take a bound. A string or a text is bounded by its length; a date's rule is usually relative to the clock, "
                    + "\"in the future\", which belongs in the domain beside the clock, not in a constant.");

                return null;
            }

            at++;

            var number = tokens[at];

            if (number.Kind != TokenKind.Number)
            {
                problems.Add(Expected(number, $"a number after '{operation.Text}'"));

                return null;
            }

            at++;

            if (Bound(name.Text, kind, precision, scale, operation.Text, number, problems) is not { } bound)
            {
                continue;
            }

            if (bound.IsLower ? lower is not null : upper is not null)
            {
                problems.Add(
                    $"--fields, at character {operation.Position + 1}: {name.Text} already has {(bound.IsLower ? "a lower" : "an upper")} "
                    + "bound; a field takes at most one of each.");

                continue;
            }

            if (bound.IsLower)
            {
                lower = bound;
            }
            else
            {
                upper = bound;
            }
        }

        var field = new Field(name.Text, kind, nullable, length, precision, scale, lower, upper, name.Position);

        if (lower is not null || upper is not null)
        {
            problems.AddRange(RangeProblems(field));
        }

        if (tokens[at].Text != "=")
        {
            if (profile.TakesDefault && !nullable)
            {
                problems.Add(
                    $"--fields, at character {tokens[at].Position + 1}: {name.Text} cannot be null, and the rows the table already "
                    + $"holds need a value for it: give one its rules accept after '=', as {field.Declaration}={DefaultExample(field)}, "
                    + "or make it nullable with '?'.");
            }

            return field;
        }

        var equals = tokens[at];

        if (!profile.TakesDefault)
        {
            problems.Add(
                $"--fields, at character {equals.Position + 1}: a default, '=' and a value, is what new-field gives the rows a table "
                + "already holds when it adds a field to them; here the field has no such rows. Leave it out.");

            return null;
        }

        if (nullable)
        {
            problems.Add(
                $"--fields, at character {equals.Position + 1}: {name.Text} is nullable, so the rows the table already holds start "
                + "with null, which it accepts; a default is for a field that cannot be null. Leave the default out, or the '?'.");

            return null;
        }

        at++;

        if (DefaultOf(field, tokens[at], problems) is not { } value)
        {
            return null;
        }

        at++;

        var defaulted = field with { Default = value };

        problems.AddRange(DefaultProblems(defaulted));

        return defaulted;
    }

    private static string DefaultExample(Field field) => field.Kind switch
    {
        FieldKind.Int or FieldKind.Long or FieldKind.Decimal => SatisfyingValue(field).ToString(CultureInfo.InvariantCulture),
        FieldKind.Bool => "false",
        FieldKind.Text => "''",
        FieldKind.DateOnly => "'2026-01-01'",
        FieldKind.DateTimeOffset => "'2026-01-01T00:00:00Z'",
        FieldKind.Guid => "'00000000-0000-0000-0000-000000000001'",
        _ => "'none'",
    };

    private static decimal SatisfyingValue(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? Step(field.Scale) : 1m;

        return field.Bounds.All(bound => bound.Holds(0m)) ? 0m
            : field.Lower is { } lower && !lower.Holds(0m) ? (lower.IsInclusive ? lower.Value : lower.Value + step)
            : field.Upper!.IsInclusive ? field.Upper.Value : field.Upper.Value - step;
    }

    private static FieldDefault? DefaultOf(Field field, Token token, List<string> problems)
    {
        string where = $"--fields, at character {token.Position + 1}";

        switch (field.Kind)
        {
            case FieldKind.Int or FieldKind.Long or FieldKind.Decimal:
                if (token.Kind != TokenKind.Number
                    || !decimal.TryParse(token.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
                {
                    problems.Add(Expected(token, $"a number after '=', the default of {field.Name}"));

                    return null;
                }

                if (field.Kind != FieldKind.Decimal)
                {
                    string type = field.Kind == FieldKind.Int ? "int" : "long";
                    decimal minimum = field.Kind == FieldKind.Int ? int.MinValue : long.MinValue;
                    decimal maximum = field.Kind == FieldKind.Int ? int.MaxValue : long.MaxValue;

                    if (token.Text.Contains('.', StringComparison.Ordinal) || number < minimum || number > maximum)
                    {
                        problems.Add($"{where}: {Shown(token.Text)} is not a whole number an {type} holds.");

                        return null;
                    }

                    string whole = number.ToString(CultureInfo.InvariantCulture);

                    return new FieldDefault(whole, field.Kind == FieldKind.Long ? whole + "L" : whole);
                }

                if (Misfit(token.Text, number, field.Precision, field.Scale) is { } misfit)
                {
                    problems.Add($"{where}: {misfit}");

                    return null;
                }

                string exact = number.ToString(CultureInfo.InvariantCulture);

                return new FieldDefault(exact, exact + "m");

            case FieldKind.Bool:
                return token.Text is "true" or "false"
                    ? new FieldDefault(token.Text, token.Text)
                    : Refused(Expected(token, $"true or false after '=', the default of {field.Name}"));

            default:
                if (token.Kind != TokenKind.Quoted)
                {
                    return Refused(token.Text == "'"
                        ? $"{where}: a quote opens the default of {field.Name} and none closes it."
                        : Expected(token, $"a value between quotes after '=', as {DefaultExample(field)}, the default of {field.Name}"));
                }

                return QuotedDefault(field, token, where, problems);
        }

        FieldDefault? Refused(string problem)
        {
            problems.Add(problem);

            return null;
        }
    }

    private static FieldDefault? QuotedDefault(Field field, Token token, string where, List<string> problems)
    {
        string text = token.Text;

        switch (field.Kind)
        {
            case FieldKind.DateOnly:
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                {
                    problems.Add($"{where}: {Shown(text)} is not a date written yyyy-MM-dd, as '2026-01-31'.");

                    return null;
                }

                return new FieldDefault($"'{date:yyyy-MM-dd}'", $"new DateOnly({date.Year}, {date.Month}, {date.Day})");

            case FieldKind.DateTimeOffset:
                if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.None, TimeSpan.FromSeconds(1))
                    || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset instant))
                {
                    problems.Add(
                        $"{where}: {Shown(text)} is not an instant written yyyy-MM-ddTHH:mm:ss with its offset, as '2026-01-31T09:00:00Z' "
                        + "or '2026-01-31T10:00:00+01:00'.");

                    return null;
                }

                var utc = instant.ToUniversalTime();

                return new FieldDefault(
                    $"'{utc:yyyy-MM-ddTHH:mm:ss}Z'",
                    $"new DateTimeOffset({utc.Year}, {utc.Month}, {utc.Day}, {utc.Hour}, {utc.Minute}, {utc.Second}, TimeSpan.Zero)");

            case FieldKind.Guid:
                if (!Guid.TryParseExact(text, "D", out Guid identifier))
                {
                    problems.Add($"{where}: {Shown(text)} is not a Guid written with its hyphens, as '3f2504e0-4f89-41d3-9a0c-0305e82c3301'.");

                    return null;
                }

                return new FieldDefault($"'{identifier:D}'", $"new Guid(\"{identifier:D}\")");

            default:
                return new FieldDefault($"'{text.Replace("'", "''", StringComparison.Ordinal)}'", Quote(text));
        }
    }

    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");

        foreach (char character in text)
        {
            quoted.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) || char.IsSurrogate(character) => $"\\u{(int)character:x4}",
                _ => character.ToString(),
            });
        }

        return quoted.Append('"').ToString();
    }

    private static IEnumerable<string> DefaultProblems(Field field)
    {
        string where = $"--fields: the default of {field.Name}, {field.Default!.Literal},";

        switch (field.Kind)
        {
            case FieldKind.Int or FieldKind.Long or FieldKind.Decimal:
                decimal value = decimal.Parse(field.Default.Literal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

                foreach (var bound in field.Bounds.Where(bound => !bound.Holds(value)))
                {
                    yield return $"{where} breaks the rule the field checks: it must be {bound.Wording} {bound.Literal}.";
                }

                break;

            case FieldKind.String or FieldKind.Text:
                string text = Unquoted(field.Default.Literal);

                if (field.Kind == FieldKind.String && text.Trim().Length == 0)
                {
                    yield return $"{where} is blank, which the field refuses: a required string holds at least one character once trimmed.";
                }
                else if (field.Kind == FieldKind.String && text != text.Trim())
                {
                    yield return $"{where} starts or ends with a space, which every write trims away: write it trimmed.";
                }

                if (text.Length > field.Length)
                {
                    yield return $"{where} is {text.Length:N0} characters long, and the field holds {field.Length:N0} at most.";
                }

                break;

            case FieldKind.DateOnly when field.Default.Literal == "'0001-01-01'":
            case FieldKind.DateTimeOffset when field.Default.Literal == "'0001-01-01T00:00:00Z'":
            case FieldKind.Guid when field.Default.Literal == $"'{Guid.Empty:D}'":
                yield return $"{where} is the empty value, which the field refuses: a required {field.ClrType} is never the default one.";

                break;
        }
    }

    private static string Unquoted(string literal) => literal[1..^1].Replace("''", "'", StringComparison.Ordinal);

    private static string? Misfit(string text, decimal value, int precision, int scale)
    {
        int decimals = text.Contains('.', StringComparison.Ordinal) ? text.Length - text.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
        int digits = decimal.Truncate(Math.Abs(value)) == 0 ? 0 : decimal.Truncate(Math.Abs(value)).ToString(CultureInfo.InvariantCulture).Length;

        return decimals > scale || digits > precision - scale
            ? $"{Shown(text)} does not fit decimal({precision},{scale}), whose values have at most {precision - scale} digits before the point and {scale} after it."
            : null;
    }

    private static int[]? Arguments(List<Token> tokens, ref int at, int count, List<string> problems)
    {
        at++;
        var values = new List<int>();

        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                if (tokens[at].Text != ",")
                {
                    problems.Add(Expected(tokens[at], "',' between the precision and the scale"));

                    return null;
                }

                at++;
            }

            var number = tokens[at];

            if (number.Kind != TokenKind.Number
                || !int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                problems.Add(Expected(number, "a whole number"));

                return null;
            }

            values.Add(value);
            at++;
        }

        if (tokens[at].Text != ")")
        {
            problems.Add(Expected(tokens[at], "')'"));

            return null;
        }

        at++;

        return [.. values];
    }

    private static FieldBound? Bound(string name, FieldKind kind, int precision, int scale, string operation, Token number, List<string> problems)
    {
        string where = $"--fields, at character {number.Position + 1}";

        if (!decimal.TryParse(number.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
        {
            problems.Add($"{where}: {Shown(number.Text)} is not a number this grammar reads.");

            return null;
        }

        if (kind is FieldKind.Int or FieldKind.Long)
        {
            decimal minimum = kind == FieldKind.Int ? int.MinValue : long.MinValue;
            decimal maximum = kind == FieldKind.Int ? int.MaxValue : long.MaxValue;
            string type = kind == FieldKind.Int ? "int" : "long";

            if (number.Text.Contains('.', StringComparison.Ordinal) || value < minimum || value > maximum)
            {
                problems.Add($"{where}: {Shown(number.Text)} is not a whole number an {type} holds.");

                return null;
            }

            if ((operation == ">=" && value == minimum) || (operation == "<=" && value == maximum))
            {
                problems.Add($"{where}: {name}{operation}{number.Text} bounds nothing: every {type} satisfies it. Leave it out.");

                return null;
            }

            return new FieldBound(operation, value, value.ToString(CultureInfo.InvariantCulture));
        }

        if (Misfit(number.Text, value, precision, scale) is { } misfit)
        {
            problems.Add($"{where}: {misfit}");

            return null;
        }

        return new FieldBound(operation, value, value.ToString(CultureInfo.InvariantCulture));
    }

    private static IEnumerable<string> RangeProblems(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? Step(field.Scale) : 1m;
        decimal floor = field.Kind switch
        {
            FieldKind.Int => int.MinValue,
            FieldKind.Long => long.MinValue,
            _ => -Ceiling(field) + step,
        };
        decimal top = field.Kind switch
        {
            FieldKind.Int => int.MaxValue,
            FieldKind.Long => long.MaxValue,
            _ => Ceiling(field) - step,
        };

        decimal low = field.Lower is { } lower ? Math.Max(floor, lower.IsInclusive ? lower.Value : lower.Value + step) : floor;
        decimal high = field.Upper is { } upper ? Math.Min(top, upper.IsInclusive ? upper.Value : upper.Value - step) : top;

        if (low > high)
        {
            yield return $"--fields: no value satisfies the bounds of {field.Declaration}. Widen them.";
        }
    }

    internal static decimal Step(int scale)
    {
        decimal step = 1m;

        for (int index = 0; index < scale; index++)
        {
            step /= 10m;
        }

        return step;
    }

    internal static decimal Ceiling(Field field)
    {
        decimal ceiling = 1m;

        for (int index = 0; index < field.Precision - field.Scale; index++)
        {
            ceiling *= 10m;
        }

        return ceiling;
    }

    internal static List<string> NameProblems(IReadOnlyList<Field> fields, FieldProfile profile)
    {
        var problems = new List<string>();

        foreach (var field in fields)
        {
            string where = $"--fields, at character {field.Position + 1}";

            if (!CSharpNames.PascalCase.IsMatch(field.Name))
            {
                problems.Add($"{where}: {Shown(field.Name)} is not a field name: PascalCase, letters and digits, as DueDate.");

                continue;
            }

            if (field.Name.Length > NameMaxLength)
            {
                problems.Add($"{where}: {field.Name} is longer than {NameMaxLength} characters, which makes names some tools cannot hold. Shorten it.");
            }

            string parameter = CSharpNames.Lower(field.Name);

            if (CSharpNames.Keywords.Contains(parameter, StringComparer.Ordinal) || _contextualKeywords.Contains(parameter, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{where}: {field.Name} becomes '{parameter}' once its first letter is lowered, which is how the generated "
                    + "parameters are named, and C# reserves that word there. Choose another name.");
            }

            if (profile.ReservedMembers.Contains(field.Name, StringComparer.Ordinal))
            {
                problems.Add($"{where}: {field.Name} is {profile.ReservedWording}. Choose another name.");
            }

            if (_reservedTypes.Contains(field.Name, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{where}: {field.Name} names a type the generated code calls by name, which a property of that name would hide. "
                    + "Choose another name.");
            }

            foreach (var other in fields.Where(other => profile.ReservesGeneratedNames && !ReferenceEquals(other, field)))
            {
                if (_generatedSuffixes.Any(suffix => field.Name == other.Name + suffix)
                    || _generatedPrefixes.Any(prefix => field.Name == prefix + other.Name))
                {
                    problems.Add(
                        $"{where}: {field.Name} is the name the generated code gives a constant, a method or a local of {other.Name}. "
                        + "Choose another name.");
                }
            }
        }

        foreach (var group in fields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            problems.Add(
                $"--fields names {string.Join(" and ", group.Select(field => field.Name))}, the same name but for its case: "
                + $"{profile.DuplicateReason}. Keep one.");
        }

        return problems;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int position = 0;

        while (position < text.Length)
        {
            char current = text[position];

            if (char.IsWhiteSpace(current))
            {
                position++;

                continue;
            }

            int start = position;

            if (char.IsAsciiLetter(current) || current == '_')
            {
                while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
                {
                    position++;
                }

                tokens.Add(new Token(TokenKind.Name, text[start..position], start));
            }
            else if (char.IsAsciiDigit(current) || (current == '-' && position + 1 < text.Length && char.IsAsciiDigit(text[position + 1])))
            {
                position++;

                while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.'))
                {
                    position++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..position], start));
            }
            else if (current is '>' or '<')
            {
                position += position + 1 < text.Length && text[position + 1] == '=' ? 2 : 1;
                tokens.Add(new Token(TokenKind.Symbol, text[start..position], start));
            }
            else if (current is ':' or ',' or '(' or ')' or '?' or '=')
            {
                position++;
                tokens.Add(new Token(TokenKind.Symbol, current.ToString(), start));
            }
            else if (current == '\'' && Closing(text, position) is var closing and > 0)
            {
                position = closing + 1;
                tokens.Add(new Token(TokenKind.Quoted, text[(start + 1)..closing].Replace("''", "'", StringComparison.Ordinal), start));
            }
            else
            {
                position++;
                tokens.Add(new Token(TokenKind.Unknown, current.ToString(), start));
            }
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));

        return tokens;
    }

    private static int Closing(string text, int opening)
    {
        for (int index = opening + 1; index < text.Length; index++)
        {
            if (text[index] != '\'')
            {
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '\'')
            {
                index++;

                continue;
            }

            return index;
        }

        return -1;
    }

    private static string Unstored(Token token, FieldProfile profile, string part) =>
        $"--fields, at character {token.Position + 1}: a field of {profile.Holder} takes no {part}. {profile.UnstoredReason}.";

    private static string Expected(Token token, string expected) =>
        $"--fields, at character {token.Position + 1}: expected {expected}, and found "
        + (token.Kind == TokenKind.End ? "the end of the list." : $"{Shown(token.Text)}.");

    private static string Shown(string text) => text.Length <= 40 ? $"'{text}'" : $"'{text[..40]}...'";
}

internal static class TextEdits
{
    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal sealed record Edit(string Path, string Anchor, string Position, string[] Lines, string[] Replaced);

    internal sealed record Command(string Display, string WorkingDirectory, string FileName, string[] Arguments)
    {
        internal bool FromLastBuild => Arguments.Contains(_noBuild, StringComparer.Ordinal);
    }

    private const string _noBuild = "--no-build";

    internal sealed class Sheet
    {
        private readonly bool _bom;

        private readonly string _newline;

        private readonly List<string> _lines;

        private readonly string[] _source;

        private readonly List<int> _origin;

        private readonly bool _sourceFinalNewline;

        internal Sheet(string path, byte[] original)
        {
            Path = path;
            Original = original;
            _bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);

            string text = _strictUtf8.GetString(original.AsSpan(_bom ? 3 : 0));

            _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            FinalNewline = text.EndsWith('\n');
            _lines = [.. (FinalNewline ? text[..^_newline.Length] : text).Split(_newline)];
            _source = [.. _lines];
            _origin = [.. Enumerable.Range(0, _lines.Count)];
            _sourceFinalNewline = FinalNewline;
        }

        internal string Path { get; }

        internal byte[] Original { get; }

        internal IReadOnlyList<string> Lines => _lines;

        internal bool FinalNewline { get; private set; }

        internal bool EndingChanged => FinalNewline != _sourceFinalNewline;

        internal string Text => string.Join('\n', _lines) + (FinalNewline ? "\n" : string.Empty);

        internal void Rewrite(string text)
        {
            bool finalNewline = text.EndsWith('\n');
            string[] next = (finalNewline ? text[..^1] : text).Split('\n');
            int head = 0;

            while (head < next.Length && head < _lines.Count && next[head] == _lines[head])
            {
                head++;
            }

            int tail = 0;

            while (tail < next.Length - head && tail < _lines.Count - head && next[^(tail + 1)] == _lines[^(tail + 1)])
            {
                tail++;
            }

            int removed = _lines.Count - head - tail;

            _lines.RemoveRange(head, removed);
            _origin.RemoveRange(head, removed);
            Insert(head, next[head..^tail]);
            FinalNewline = finalNewline;
        }

        internal void Insert(int index, string[] lines)
        {
            _lines.InsertRange(index, lines);
            _origin.InsertRange(index, lines.Select(_ => -1));
        }

        internal IEnumerable<Edit> Edits()
        {
            int previous = -1;
            int previousSource = -1;

            for (int index = 0; index <= _lines.Count; index++)
            {
                if (index < _lines.Count && _origin[index] < 0)
                {
                    continue;
                }

                int nextSource = index < _lines.Count ? _origin[index] : _source.Length;
                string[] added = [.. _lines.Skip(previous + 1).Take(index - previous - 1)];
                string[] replaced = _source[(previousSource + 1)..nextSource];

                if (added.Length > 0 || replaced.Length > 0)
                {
                    int next = index < _lines.Count ? index : -1;
                    bool before = next >= 0 && (_lines[next].Trim().Length > 0 || previous < 0 || _lines[previous].Trim().Length == 0);

                    yield return new Edit(Path, _lines[before ? next : previous], before ? "before" : "after", added, replaced);
                }

                previous = index;
                previousSource = nextSource;
            }
        }

        internal byte[] Bytes()
        {
            byte[] text = Encoding.UTF8.GetBytes(string.Join(_newline, Lines) + (FinalNewline ? _newline : string.Empty));

            return _bom ? [.. Encoding.UTF8.Preamble, .. text] : text;
        }
    }

    internal sealed class Editor(string root)
    {
        private readonly Dictionary<string, Sheet?> _sheets = new(StringComparer.Ordinal);

        internal string Step { get; set; } = string.Empty;

        internal IReadOnlyList<Edit> Applied =>
            _sheets.Values.OfType<Sheet>().Any(sheet => sheet.EndingChanged)
                ? throw new InvalidOperationException("An edit that adds or removes the newline that ends a file cannot be recorded to be undone.")
                : [.. _sheets.Values.OfType<Sheet>().SelectMany(sheet => sheet.Edits())];

        internal List<string> Kept { get; } = [];

        internal List<string> Failures { get; } = [];

        internal IEnumerable<Sheet> Changed =>
            _sheets.Values.OfType<Sheet>().Where(sheet => !sheet.Original.AsSpan().SequenceEqual(sheet.Bytes()));

        internal Sheet? Open(string path)
        {
            if (!_sheets.TryGetValue(path, out Sheet? sheet))
            {
                string full = System.IO.Path.Combine(root, path);

                sheet = File.Exists(full) ? new Sheet(path, File.ReadAllBytes(full)) : null;
                _sheets[path] = sheet;
            }

            if (sheet is null)
            {
                Fail($"{path} does not exist.");
            }

            return sheet;
        }

        internal void Fail(string reason) => Failures.Add(Step.Length == 0 ? reason : $"{Step}: {reason}");

        internal static void Insert(Sheet sheet, int anchor, bool before, string[] lines) => sheet.Insert(before ? anchor : anchor + 1, lines);

        internal static int Find(Sheet sheet, Func<string, bool> match, int from = 0, int to = -1)
        {
            int end = to < 0 ? sheet.Lines.Count : Math.Min(to, sheet.Lines.Count);

            for (int index = Math.Max(from, 0); index < end; index++)
            {
                if (match(sheet.Lines[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        internal static int FindLast(Sheet sheet, Func<string, bool> match, int from)
        {
            for (int index = Math.Min(from, sheet.Lines.Count - 1); index >= 0; index--)
            {
                if (match(sheet.Lines[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        internal void Place(string path, string[] anchors, bool before, string[] lines, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line.Trim().StartsWith(present, StringComparison.Ordinal)) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int anchor = anchors
                .Select(text => Find(sheet, line => line.Trim().StartsWith(text, StringComparison.Ordinal)))
                .FirstOrDefault(index => index >= 0, -1);

            if (anchor < 0)
            {
                Fail($"no line starts with '{string.Join("' or '", anchors)}', the line the edit is placed beside.");

                return;
            }

            Insert(sheet, anchor, before, lines);
        }

        internal void Rewrite(string path, Func<string, string?> edit)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (edit(sheet.Text) is { } text)
            {
                sheet.Rewrite(text);
            }
            else
            {
                Fail($"its anchor is not in {path}, so nothing could be written.");
            }
        }

        internal void PlaceInEachMethod(string path, string anchor, bool before, string line)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            int found = 0;

            for (int index = 0; index < sheet.Lines.Count; index++)
            {
                if (sheet.Lines[index].Trim() != anchor)
                {
                    continue;
                }

                found++;

                int start = FindLast(sheet, candidate => candidate == "    {", index);
                int end = Find(sheet, candidate => candidate == "    }", index);

                if (Find(sheet, candidate => candidate.Trim() == line.Trim(), start, end) >= 0)
                {
                    Kept.Add(Step);

                    continue;
                }

                Insert(sheet, index, before, [line]);
                index++;
            }

            if (found == 0)
            {
                Fail($"no line reads '{anchor}', the line the edit is placed beside in each method.");
            }
        }

        internal void AppendToMethod(string path, string method, string prefix, string line)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            int declaration = Find(sheet, candidate => candidate.Contains(method, StringComparison.Ordinal));
            int start = declaration < 0 ? -1 : Find(sheet, candidate => candidate == "    {", declaration);
            int end = start < 0 ? -1 : Find(sheet, candidate => candidate == "    }", start);

            if (end < 0)
            {
                Fail($"no method{method} with a body was found to take the line.");

                return;
            }

            if (Find(sheet, candidate => candidate.Trim() == line.Trim(), start, end) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int last = FindLast(sheet, candidate => candidate.Trim().StartsWith(prefix, StringComparison.Ordinal), end);

            if (last <= start)
            {
                Fail($"the method{method} has no line starting with '{prefix}' to place the line after.");

                return;
            }

            Insert(sheet, last, before: false, [line]);
        }

        internal void Using(string path, string space)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            string directive = $"using {space};";
            int body = Find(sheet, line => !IsHeader(line));
            int[] usings = [.. Enumerable.Range(0, body < 0 ? sheet.Lines.Count : body).Where(index => IsUsing(sheet.Lines[index]))];
            string? own = sheet.Lines
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal) && line.EndsWith(';'))?["namespace ".Length..^1];

            if (usings.Any(index => sheet.Lines[index].Trim() == directive)
                || (own is not null && (own == space || own.StartsWith(space + ".", StringComparison.Ordinal))))
            {
                return;
            }

            int following = usings.FirstOrDefault(index => Order(space, sheet.Lines[index].Trim()["using ".Length..].TrimEnd(';')) < 0, -1);

            if (following >= 0)
            {
                Insert(sheet, following, before: true, [directive]);
            }
            else if (usings.Length > 0)
            {
                Insert(sheet, usings[^1], before: false, [directive]);
            }
            else
            {
                Insert(sheet, 0, before: true, [directive, string.Empty]);
            }
        }

        internal void Row(string path, string heading, string row, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line.Contains(present, StringComparison.Ordinal)) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int level = heading.TakeWhile(character => character == '#').Count();
            int section = Find(sheet, line => line == heading);
            int next = section < 0 ? -1 : Find(sheet, line => line.StartsWith('#') && line.TakeWhile(character => character == '#').Count() <= level, section + 1);
            int rule = section < 0 ? -1 : Find(sheet, line => line.StartsWith("|---", StringComparison.Ordinal), section + 1, next);

            if (rule < 0)
            {
                Fail($"{path} has no table under '{heading}' to take the row.");

                return;
            }

            int last = rule;

            while (last + 1 < sheet.Lines.Count && sheet.Lines[last + 1].StartsWith('|'))
            {
                last++;
            }

            Insert(sheet, last, before: false, [row]);
        }

        internal void Section(string path, string heading, string[] lines, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line == present) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int section = Find(sheet, line => line == heading);
            int next = section < 0 ? -1 : Find(sheet, line => line.StartsWith("## ", StringComparison.Ordinal), section + 1);

            if (next < 0)
            {
                Fail($"{path} has no '{heading}' section followed by another, before which the section is placed.");

                return;
            }

            Insert(sheet, next, before: true, lines);
        }

        private static bool IsUsing(string line) =>
            line.StartsWith("using ", StringComparison.Ordinal) && line.TrimEnd().EndsWith(';') && !line.Contains('=', StringComparison.Ordinal);

        private static bool IsHeader(string line) =>
            line.Trim().Length == 0 || line.TrimStart().StartsWith("//", StringComparison.Ordinal) || IsUsing(line);

        private static int Order(string left, string right)
        {
            static bool IsSystem(string space) => space == "System" || space.StartsWith("System.", StringComparison.Ordinal);

            int system = IsSystem(right).CompareTo(IsSystem(left));

            return system != 0 ? system
                : StringComparer.OrdinalIgnoreCase.Compare(left, right) is var ignoringCase and not 0 ? ignoringCase
                : StringComparer.Ordinal.Compare(left, right);
        }
    }

    internal static void Commit(string root, IReadOnlyList<(string Path, byte[] Bytes)> created, IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> replaced, IReadOnlyList<string>? deleted = null)
    {
        var written = new List<string>();
        var directories = new List<string>();
        var overwritten = new List<(string Full, byte[] Original)>();
        var setAside = new List<(string Full, string Aside)>();
        string? staged = null;

        try
        {
            foreach (var (path, bytes) in created)
            {
                string full = System.IO.Path.Combine(root, path);
                var missing = new Stack<string>();

                for (string next = System.IO.Path.GetDirectoryName(full)!; !Directory.Exists(next); next = System.IO.Path.GetDirectoryName(next)!)
                {
                    missing.Push(next);
                }

                directories.AddRange(missing);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);

                using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);

                written.Add(full);
                stream.Write(bytes);
            }

            foreach (var (path, original, bytes) in replaced)
            {
                string full = System.IO.Path.Combine(root, path);

                staged = $"{full}.{Guid.NewGuid():N}.tmp";

                using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(bytes);
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(staged, File.GetUnixFileMode(full));
                }

                File.Move(staged, full, overwrite: true);
                staged = null;
                overwritten.Add((full, original));
            }

            foreach (string path in deleted ?? [])
            {
                string full = System.IO.Path.Combine(root, path);
                string aside = $"{full}.{Guid.NewGuid():N}.tmp";

                File.Move(full, aside);
                setAside.Add((full, aside));
            }
        }
        catch
        {
            foreach (var (full, aside) in Enumerable.Reverse(setAside))
            {
                File.Move(aside, full);
            }

            if (staged is not null && File.Exists(staged))
            {
                File.Delete(staged);
            }

            foreach (var (full, original) in overwritten)
            {
                File.WriteAllBytes(full, original);
            }

            foreach (string full in written.Where(File.Exists))
            {
                File.Delete(full);
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

        foreach (var (_, aside) in setAside)
        {
            File.Delete(aside);
        }
    }

    internal static byte[] Encode(string path, string content)
    {
        byte[] text = Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n"));

        return path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.Ordinal)
            ? [.. Encoding.UTF8.GetPreamble(), .. text]
            : text;
    }

    internal static IEnumerable<(string Path, byte[] Original, byte[] Bytes)> Replacements(Editor editor) =>
        editor.Changed.Select(sheet => (sheet.Path, sheet.Original, sheet.Bytes()));

    internal static Command Task(string root, params string[] task) =>
        new($"dotnet run Tools/Tasks.cs {string.Join(' ', task)}", root, "dotnet", ["run", "Tools/Tasks.cs", .. task]);

    internal static Command FromLastBuild(string root, params string[] task) => Task(root, ["--", .. task, _noBuild]);

    internal static List<Command> ContractSteps(string root, string client, bool built) =>
    [
        built ? FromLastBuild(root, "openapi") : Task(root, "openapi"),
        .. Directory.Exists(System.IO.Path.Combine(root, "Src", "Clients", client)) ? [Task(root, "api-client")] : Array.Empty<Command>(),
        .. File.Exists(System.IO.Path.Combine(root, "Src", "Web", "packages", "api-client", "scripts", "generate.mjs"))
            ? [new Command($"cd Src/Web && pnpm api {_noBuild}", System.IO.Path.Combine(root, "Src", "Web"), OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", ["api", _noBuild])]
            : Array.Empty<Command>(),
    ];

    internal static int Execute(Command command)
    {
        var start = new System.Diagnostics.ProcessStartInfo(command.FileName) { WorkingDirectory = command.WorkingDirectory, UseShellExecute = false };

        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(start)!;

            process.WaitForExit();

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Console.Error.WriteLine($"{command.FileName} could not be started: {exception.Message}");

            return 127;
        }
    }

    internal static bool RunAll(IReadOnlyList<Command> steps, Func<Command, int> run, TextWriter output, TextWriter error, string done)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            output.WriteLine();
            output.WriteLine($"Running {steps[index].Display}");
            output.Flush();

            int code = run(steps[index]);

            if (code == 0)
            {
                continue;
            }

            error.WriteLine($"error: `{steps[index].Display}` failed with exit code {code}.");
            error.WriteLine($"Done: {done}{string.Concat(steps.Take(index).Select(step => $", then `{step.Display}`"))}.");
            error.WriteLine("What remains, in this order, once the failure is fixed:");

            foreach (Command remaining in steps.Skip(index))
            {
                error.WriteLine($"  {remaining.Display}");
            }

            if (steps.Skip(index).Any(remaining => remaining.FromLastBuild))
            {
                error.WriteLine(
                    $"A command with {_noBuild} works from the build this command made: when the fix changes the code, run "
                    + "`dotnet run Tools/Tasks.cs build` before it.");
            }

            return false;
        }

        output.WriteLine();
        output.WriteLine($"Done: {done}{string.Concat(steps.Select(step => $", then `{step.Display}`"))}.");

        return true;
    }
}

internal sealed class FeatureManifest
{
    internal const string Folder = ".feature-manifest";

    internal const int EditsVersion = 2;

    private readonly JsonObject _top;

    private readonly SortedDictionary<string, string> _files = new(StringComparer.Ordinal);

    private readonly List<TextEdits.Edit> _edits;

    private readonly int _editsRead;

    private FeatureManifest(string path, JsonObject top, byte[]? original, List<TextEdits.Edit> edits)
    {
        Path = path;
        Original = original;
        _top = top;
        _edits = edits;
        _editsRead = edits.Count;

        foreach (var file in ((JsonArray)top["files"]!).OfType<JsonObject>())
        {
            _files[file["path"]!.GetValue<string>()] = file["sha256"]!.GetValue<string>();
        }
    }

    internal string Path { get; }

    internal byte[]? Original { get; }

    internal int? SchemaVersion =>
        _top["schemaVersion"] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out int version) ? version : null;

    internal string? Generator => Text(_top["generator"]);

    internal string? Feature => Text(_top["feature"]);

    internal string? Type => Text(_top["type"]);

    internal string? Page => Text(_top["page"]);

    internal JsonObject? Options => _top["options"] as JsonObject;

    internal JsonArray? Fields => _top["fields"] as JsonArray;

    internal IReadOnlyDictionary<string, string> Files => _files;

    internal IReadOnlyList<TextEdits.Edit> Edits => _edits;

    internal IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields =>
        [
            .. (_top["addedFields"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(addition => Text(addition["declaration"]) is not null && Text(addition["migration"]) is not null)
                .Select(addition => (
                    Text(addition["declaration"])!,
                    Text(addition["migration"])!,
                    (string[])[.. (addition["files"] as JsonArray ?? []).OfType<JsonObject>().Select(file => Text(file["path"])).OfType<string>()])),
        ];

    internal (string Path, byte[] Original, byte[] Bytes) Replacement => (Path, Original!, Bytes());

    internal static string PathOf(string features) => $"{Folder}/{features}.json";

    internal static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static FeatureManifest Create(string generator, string features, string type, JsonObject options, JsonArray? fields, string? page)
    {
        var top = new JsonObject
        {
            ["schemaVersion"] = EditsVersion,
            ["generator"] = generator,
            ["feature"] = features,
            ["type"] = type,
            ["options"] = options,
        };

        if (fields is not null)
        {
            top["fields"] = fields;
        }

        top["files"] = new JsonArray();

        if (page is not null)
        {
            top["page"] = page;
        }

        top["edits"] = new JsonArray();

        return new FeatureManifest(PathOf(features), top, null, []);
    }

    internal static FeatureManifest? Read(string root, string features, out string? problem)
    {
        string path = PathOf(features);
        string full = System.IO.Path.Combine(root, path);

        problem = null;

        if (!File.Exists(full))
        {
            return null;
        }

        byte[] original;

        try
        {
            original = File.ReadAllBytes(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }

        try
        {
            if (JsonNode.Parse(original) is not JsonObject top
                || Text(top["feature"]) != features
                || top["files"] is not JsonArray files
                || files.Any(file => file is not JsonObject entry || Text(entry["path"]) is null || Text(entry["sha256"]) is null)
                || (top["edits"] is { } edits && edits is not JsonArray))
            {
                problem = $"does not read as the manifest of {features}";

                return null;
            }

            var read = new List<TextEdits.Edit>();

            foreach (var node in top["edits"] as JsonArray ?? [])
            {
                if (node is not JsonObject edit
                    || Text(edit["path"]) is not { } editPath
                    || Text(edit["anchor"]) is not { } anchor
                    || Text(edit["position"]) is not ("before" or "after") || edit["lines"] is not JsonArray lines
                    || lines.Any(line => Text(line) is null)
                    || (edit["replaced"] is { } replacedNode && (replacedNode is not JsonArray replacedLines || replacedLines.Any(line => Text(line) is null))))
                {
                    problem = $"does not read as the manifest of {features}: an edit names no file, anchor, position or lines";

                    return null;
                }

                read.Add(new TextEdits.Edit(
                    editPath,
                    anchor,
                    Text(edit["position"])!,
                    [.. lines.Select(line => Text(line)!)],
                    [.. (edit["replaced"] as JsonArray ?? []).Select(line => Text(line)!)]));
            }

            return new FeatureManifest(path, top, original, read);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }
    }

    internal void Record(string path, byte[] bytes) => _files[path] = Sha256(bytes);

    internal void RecordAddition(string declaration, string migration, IEnumerable<(string Path, byte[]? Before, byte[] After, IReadOnlyList<string> Changes)> files)
    {
        var additions = _top["addedFields"] as JsonArray ?? [];

        additions.Add((JsonNode)new JsonObject
        {
            ["declaration"] = declaration,
            ["migration"] = migration,
            ["files"] = new JsonArray([.. files.Select(file => (JsonNode)new JsonObject
            {
                ["path"] = file.Path,
                ["before"] = file.Before is null ? null : Sha256(file.Before),
                ["after"] = Sha256(file.After),
                ["changes"] = new JsonArray([.. file.Changes.Select(change => (JsonNode)JsonValue.Create(change)!)]),
            })]),
        });
        _top["addedFields"] = additions;
    }

    internal void Record(TextEdits.Editor editor)
    {
        foreach (var sheet in editor.Changed)
        {
            if (_files.TryGetValue(sheet.Path, out string? recorded) && recorded == Sha256(sheet.Original))
            {
                _files[sheet.Path] = Sha256(sheet.Bytes());
            }
        }

        _edits.AddRange(editor.Applied.Where(edit => !_files.ContainsKey(edit.Path)));
    }

    internal byte[] Bytes()
    {
        var files = new JsonArray();

        foreach (var (path, sha256) in _files)
        {
            files.Add((JsonNode)new JsonObject { ["path"] = path, ["sha256"] = sha256 });
        }

        _top["files"] = files;

        var edits = new JsonArray();

        foreach (var edit in _edits)
        {
            var written = new JsonObject
            {
                ["path"] = edit.Path,
                ["anchor"] = edit.Anchor,
                ["position"] = edit.Position,
                ["lines"] = new JsonArray([.. edit.Lines.Select(line => (JsonNode)JsonValue.Create(line)!)]),
            };

            if (edit.Replaced.Length > 0)
            {
                written["replaced"] = new JsonArray([.. edit.Replaced.Select(line => (JsonNode)JsonValue.Create(line)!)]);
            }

            edits.Add((JsonNode)written);
        }

        if (_edits.Count > _editsRead)
        {
            _top["schemaVersion"] = Math.Max(SchemaVersion ?? 1, EditsVersion);
            _top.Remove("edits");
            _top["edits"] = edits;
        }
        else if (_top.ContainsKey("edits"))
        {
            _top["edits"] = edits;
        }

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            _top.WriteTo(writer);
        }

        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
