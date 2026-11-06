using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.AddEvent;

internal static class NewEvent
{
    internal const string Usage =
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

    private sealed record Listener(string Features, string Reaction);

    internal static int Run(AddEventRequest request, GenerationContext context)
    {
        var problems = new List<string>();
        var spec = Check(request, context.Root, context.Product, problems);

        return spec is null
            ? Refusal.Write(context.Error, problems, Usage)
            : new EventGeneration(spec, context).Generate();
    }

    private static EventSpec? Check(AddEventRequest request, string root, string product, List<string> problems)
    {
        CheckNames(request.Features, request.Name, problems);
        CheckListener(request, problems);
        var listener = request.Consumer is { } consumer ? ReadListener(consumer, problems) : null;

        return problems.Count > 0 ? null : CheckTree(request, listener, root, product, problems);
    }

    private static void CheckNames(string features, string name, List<string> problems)
    {
        if (!CSharpNames.PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!CSharpNames.PascalCase.IsMatch(name))
        {
            problems.Add($"'{name}' is not an event name: <Event> is PascalCase, e.g. InvoicePaidDomainEvent.");
        }
        else if (!name.EndsWith(EventSpec.Suffix, StringComparison.Ordinal) || name.Length == EventSpec.Suffix.Length)
        {
            problems.Add($"'{name}' does not end in {EventSpec.Suffix} after a name of its own: every event of a feature does, as TodoItemCompletedDomainEvent does. Name the fact, e.g. InvoicePaid{EventSpec.Suffix}.");
        }
        else if (name.Length > 60)
        {
            problems.Add("An event name longer than 60 characters makes a path that some tools cannot open. Shorten it.");
        }
    }

    private static void CheckListener(AddEventRequest request, List<string> problems)
    {
        if (request.Consumer is null == (request.Unconsumed is null))
        {
            problems.Add(
                request.Consumer is null
                    ? "Say who listens: --consumer <Features>:<Reaction>, or --unconsumed \"<reason>\" when nothing does. An event is consumed or listed as unconsumed with its reason (ADR 0006), and one of the two is required."
                    : "--consumer and --unconsumed exclude each other: an event with a consumer is not listed as unconsumed.");
        }

        if (request.Unconsumed is { } reason && !IsOneLineReason(reason))
        {
            problems.Add("--unconsumed is the reason in one line of 8 to 120 characters, with no '//' in it, written beside the event's name as a comment.");
        }
    }

    private static bool IsOneLineReason(string reason) =>
        reason.Trim().Length >= 8 && reason.Length <= 120 && !reason.Any(char.IsControl) && !reason.Contains("//", StringComparison.Ordinal);

    private static Listener? ReadListener(string consumer, List<string> problems)
    {
        string[] parts = consumer.Split(':');

        if (parts.Length != 2 || !CSharpNames.PascalCase.IsMatch(parts[0]) || !CSharpNames.PascalCase.IsMatch(parts[1]))
        {
            problems.Add($"--consumer '{consumer}' is not written <Features>:<Reaction>, both PascalCase, e.g. Reminders:CancelReminders.");

            return null;
        }

        string reaction = parts[1];

        if (reaction.Length > 40 || reaction.EndsWith("Consumer", StringComparison.Ordinal))
        {
            problems.Add($"'{reaction}' is what the consumer does, a verb and its subject of at most 40 characters without 'Consumer': the class is {reaction}On<Event>Consumer.");
        }

        return new Listener(parts[0], reaction);
    }

    private static EventSpec? CheckTree(AddEventRequest request, Listener? listener, string root, string prefix, List<string> problems)
    {
        string features = request.Features;
        string name = request.Name;
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

        bool? takes = listener is null ? false : ConsumerTakesConfiguration(root, prefix, name, listener, problems);

        if (takes is null)
        {
            return null;
        }

        var manifest = FeatureManifest.Read(root, features, out string? unread);

        if (ManifestUnread(features, unread, problems))
        {
            return null;
        }

        return new EventSpec(prefix, features, name, fields, listener?.Features, listener?.Reaction, takes.Value, request.Unconsumed?.Trim(), manifest?.Path);
    }

    private static bool? ConsumerTakesConfiguration(string root, string prefix, string name, Listener listener, List<string> problems)
    {
        string application = Full(root, SourceProject(prefix, "Application"));

        if (!Directory.Exists(Path.Combine(application, "Features", listener.Features)))
        {
            problems.Add($"No feature called {listener.Features} to react: '{SourceProject(prefix, "Application")}/Features/{listener.Features}' does not exist.");

            return null;
        }

        string eventBase = name[..^EventSpec.Suffix.Length];
        string consumerFile = Path.Combine(application, "Features", listener.Features, "Services", "Consumers", eventBase, $"{listener.Reaction}On{eventBase}Consumer.cs");

        if (File.Exists(consumerFile))
        {
            problems.Add($"{Path.GetRelativePath(root, consumerFile).Replace('\\', '/')} exists. A consumer is written once; after that it is edited.");

            return null;
        }

        string module = Path.Combine(application, "ApplicationModule.cs");

        return File.Exists(module)
            && Regex.Match(File.ReadAllText(module), $@"public static IServiceCollection Add{listener.Features}\(this IServiceCollection services(?<rest>[^)]*)\)", RegexOptions.None, TimeSpan.FromSeconds(2)) is { Success: true } method
            && Regex.IsMatch(method.Groups["rest"].Value, @"IConfiguration configuration\b", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private static bool ManifestUnread(string features, string? unread, List<string> problems)
    {
        if (unread is null)
        {
            return false;
        }

        problems.Add($"'{FeatureManifest.PathOf(features)}' {unread}, so the files written could not be recorded in it. Repair or delete it first.");

        return true;
    }

    private static List<Field> ReadFields(string? text, string name, string domain, string features, List<string> problems)
    {
        if (text is null)
        {
            return AggregateId(domain, features, problems);
        }

        if (FieldGrammar.Parse(text, FieldProfile.Event, problems) is not { } parsed)
        {
            return [];
        }

        if (parsed.FirstOrDefault(field => field.Name == name) is { } own)
        {
            problems.Add($"--fields, at character {own.Position + 1}: {name} is the event's own name, which a property of it cannot share. Choose another name.");
        }

        if (problems.Count == 0 && parsed.Count > 12)
        {
            problems.Add("An event carries at most 12 fields; one that needs more is a record to look up by id.");
        }

        return [.. parsed];
    }

    private static List<Field> AggregateId(string domain, string features, List<string> problems)
    {
        string[] found = DomainFolders.Aggregates(domain);

        if (found.Length != 1)
        {
            problems.Add(
                $"--fields is needed: {features} declares {(found.Length == 0 ? "no repository" : "several repositories, " + string.Join(", ", found))} under Repositories/, so there is no single aggregate whose id the event carries by default. "
                + "Pass --fields \"InvoiceId:Guid\".");

            return [];
        }

        return [new Field(found[0] + "Id", FieldKind.Guid, Nullable: false, Length: 0, Precision: 0, Scale: 0, Lower: null, Upper: null, Position: 0)];
    }
}
