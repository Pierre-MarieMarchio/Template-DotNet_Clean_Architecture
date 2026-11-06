using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.AddEvent;
using CleanArch.Cli.Generators.Shared;
using Xunit;
using static CleanArch.Cli.Generators.AddEvent.NewEvent;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Tests.Generators;

public sealed class NewEventTests
{
    [Fact]
    public void Parser() => Expectations.Hold(ParserCases);

    [Fact]
    public void Field() => Expectations.Hold(FieldCases);

    [Fact]
    public void Consumed() => Expectations.Hold(ConsumedCases);

    [Fact]
    public void Unconsumed() => Expectations.Hold(UnconsumedCases);

    [Fact]
    public void Anchor() => Expectations.Hold(AnchorCases);

    [Fact]
    public void Tree() => Expectations.Hold(TreeCases);

    [Fact]
    public void Rollback() => Expectations.Hold(RollbackCases);

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

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args) =>
        GeneratorRun.Invoke(AddEventCommand.Build(GeneratorRun.Project(root), _ => new FakeExecutor(_ => 0)), args);

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
