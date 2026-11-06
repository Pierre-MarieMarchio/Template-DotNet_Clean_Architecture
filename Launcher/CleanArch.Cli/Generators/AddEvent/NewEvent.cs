using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.AddEvent;

internal static class NewEvent
{
    internal const string _conditionOpen = "#" + "if (";

    internal const string _conditionClose = "#" + "endif";

    private const string _suffix = "DomainEvent";

    private const string _usage =
        "Usage: dotnet cleanarch add event <Features> <Event> (--consumer <Features>:<Reaction> | --unconsumed \"<reason>\")\n"
        + "           [--fields \"Name:Type, Other:Type\"] [--dry-run]\n"
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
        + "  --dry-run     print what would be written, and write nothing.";

    private static readonly string[] _optionNames = ["--consumer", "--unconsumed", "--fields", "--root", "--dry-run"];

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

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var problems = new List<string>();
        var request = Parse(args, problems);
        string root = Path.GetFullPath(request.Root);
        var spec = problems.Count == 0 ? Check(request, root, problems) : null;

        return spec is null
            ? Refusal.Write(error, problems, _usage)
            : new EventGeneration(spec, new GenerationContext(root, request.DryRun, output, error)).Generate();
    }

    private sealed class EventGeneration(Spec spec, GenerationContext context) : FileGeneration(context, spec.Features, spec.Manifest)
    {
        protected override string Usage => _usage;

        protected override IReadOnlyList<Emission> Plan() => NewEvent.Emissions(spec, Root);

        protected override TextEdits.Editor Edit(List<string> problems) => Edits(spec, Root, problems);

        protected override void Report(ReportMode mode) =>
            NewEvent.Report(Output, spec, Emissions, Editor.Changed.Select(sheet => sheet.Path), mode == ReportMode.DryRun);
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
            prefix = ProductName(root);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }

        string domain = Full(root, $"{SourceProject(prefix, "Domain")}/Features/{features}");

        if (!Directory.Exists(domain))
        {
            problems.Add(
                $"{features} has no domain folder '{SourceProject(prefix, "Domain")}/Features/{features}', so it is a service over a store and raises no domain event. "
                + "Promote it to an aggregate first (docs/architecture/feature-shapes.md, From simple to rich).");

            return null;
        }

        var fields = ReadFields(request.Fields, name, domain, features, problems);

        if (problems.Count > 0)
        {
            return null;
        }

        if (SourceFiles(Path.Combine(root, Src), "*.cs").Any(file => Regex.IsMatch(File.ReadAllText(file), $@"\b(?:record|class) {name}\b", RegexOptions.None, TimeSpan.FromSeconds(2))))
        {
            problems.Add($"A type called {name} already exists under Src. An event is written once; after that it is edited.");

            return null;
        }

        bool takes = false;

        if (consumerFeatures is not null)
        {
            string application = Full(root, SourceProject(prefix, "Application"));

            if (!Directory.Exists(Path.Combine(application, "Features", consumerFeatures)))
            {
                problems.Add($"No feature called {consumerFeatures} to react: '{SourceProject(prefix, "Application")}/Features/{consumerFeatures}' does not exist.");

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
        string domain = $"{SourceProject(spec.Prefix, "Domain")}/Features/{spec.Features}/Model/Events";
        var emissions = new List<Emission> { new($"{domain}/{spec.Name}.cs", EventRecord(spec)) };
        string domainTests = $"Tests/Domain/{FolderOf(spec.Prefix, "Domain.UnitTests")}";

        if (Directory.Exists(Path.Combine(root, domainTests)))
        {
            emissions.Add(new Emission($"{domainTests}/Features/{spec.Features}/Model/Events/{spec.Name}Tests.cs", EventTests(spec)));
        }

        if (spec.ConsumerFeatures is not null)
        {
            string application = $"{SourceProject(spec.Prefix, "Application")}/Features/{spec.ConsumerFeatures}/Services/Consumers/{spec.Base}";

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
            string module = $"{SourceProject(spec.Prefix, "Application")}/ApplicationModule.cs";
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

    private static void Report(TextWriter output, Spec spec, IReadOnlyList<Emission> emissions, IEnumerable<string> edited, bool dryRun)
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

}
