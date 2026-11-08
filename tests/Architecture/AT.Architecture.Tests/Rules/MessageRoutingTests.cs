using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class MessageRoutingTests
{
    [Fact]
    public void EveryMessage_HasExactlyOneHandler()
    {
        var messages = UseCaseTypes.MessagesInTheApplicationLayer;

        messages.ShouldNotBeEmpty("No message was found in the application layer, so this rule reads nothing.");

        HandlerCountFailures(
                messages,
                UseCaseTypes.InTheApplicationLayer.Select(handler =>
                    (handler, UseCaseTypes.ContractOf(handler).GetGenericArguments()[0])))
            .ShouldBeEmpty(
                "A message is sent to exactly one handler: with none it can be sent and answered by " +
                "nothing, with two the one that answers is a guess. Each host's generated mediator " +
                "refuses both at build time; this reads the same rule off the application layer.");
    }

    [Fact]
    public void TheHandlerCount_IsSensitive_AndSeesAMissingAndASecondHandler()
    {
        Type[] messages = [typeof(Unhandled), typeof(HandledOnce), typeof(HandledTwice)];

        HandlerCountFailures(
                messages,
                [(typeof(FirstHandler), typeof(HandledOnce)), (typeof(FirstHandler), typeof(HandledTwice)), (typeof(SecondHandler), typeof(HandledTwice))])
            .ShouldBe(
                [$"{typeof(HandledTwice).FullName} has 2 handlers", $"{typeof(Unhandled).FullName} has 0 handlers"],
                "The count must see a message nothing handles and a message two classes handle, and " +
                "nothing in the message handled once.");
    }
    //#if (api)

    [Fact]
    public void EveryMessage_IsSentByAHostOrAJob()
    {
        var named = NamesInTheHostsCode();

        named.ShouldNotBeEmpty("No host source was read, so every message would read as unsent.");

        UseCaseTypes.MessagesInTheApplicationLayer
            .Where(message => !named.Contains(message.Name))
            .Select(message => message.FullName ?? message.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A message no host and no job sends is an operation no caller can reach. Send it from " +
                "the controller, tool, page or job that offers it, through ISender, or remove it.");
    }

    [Fact]
    public void NoTwoMessages_ShareAShortName()
    {
        SharedShortNames(UseCaseTypes.MessagesInTheApplicationLayer).ShouldBeEmpty(
            "The rule above reads the hosts' code by name, not by type, so of two messages sharing a " +
            "short name, sending one reads as sending both. Rename one message of each pair named here.");
    }

    [Fact]
    public void TheSharedNameRule_IsSensitive_AndSeesTwoFeaturesUsingOneMessageName()
    {
        SharedShortNames([typeof(Widgets.ArchiveCommand), typeof(Gadgets.ArchiveCommand), typeof(HandledOnce)])
            .ShouldBe([$"ArchiveCommand: {typeof(Gadgets.ArchiveCommand).FullName}, {typeof(Widgets.ArchiveCommand).FullName}"]);
    }

    [Theory]
    [InlineData("// await sender.Send(new RenameWidgetCommand(id), cancellationToken);")]
    [InlineData("/* RenameWidgetCommand */ await sender.Send(command, cancellationToken);")]
    [InlineData("logger.LogInformation(\"RenameWidgetCommand\");")]
    [InlineData("logger.LogInformation(nameof(RenameWidgetCommand));")]
    [InlineData("using static AppTemplate.Application.Features.Widgets.UseCases.RenameWidget.RenameWidgetCommand;")]
    [InlineData("using Rename = AppTemplate.Application.Features.Widgets.UseCases.RenameWidget.RenameWidgetCommand;")]
    public void TheSendingMatcher_DoesNotTakeAMentionForASend(string source) =>
        Names(source, "RenameWidgetCommand").ShouldBeFalse(
            "A message named in a comment, a string, a nameof or a using directive is not sent by it.");

    [Fact]
    public void TheSendingMatcher_IsSensitive_AndTellsAMessageFromALongerName()
    {
        Names("await sender.Send(new RenameWidgetCommand(id), cancellationToken);", "RenameWidgetCommand")
            .ShouldBeTrue("A message constructed where it is sent is named.");

        Names("services.AddMaintenanceTask<PurgeWidgetsCommand>(", "PurgeWidgetsCommand")
            .ShouldBeTrue("A job typed by the command it sends names it.");

        Names("await sender.Send(new RenameWidgetCommandV2(id), cancellationToken);", "RenameWidgetCommand")
            .ShouldBeFalse("A longer name that starts with the message's is another type.");
    }

    private static bool Names(string source, string message) =>
        SourceSyntax.NamesUsedInCode(source).Contains(message, StringComparer.Ordinal);

    private static List<string> SharedShortNames(IEnumerable<Type> messages) =>
        [.. messages
            .GroupBy(message => message.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: " + string.Join(", ", group.Select(message => message.FullName).Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)];

    private static HashSet<string> NamesInTheHostsCode() =>
        ProjectReferenceGraph.Hosts
            .Select(ProjectReferenceGraph.RootOf)
            .Concat(StarterHosts())
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => SourceSyntax.NamesUsedInCode(File.ReadAllText(file)))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> StarterHosts()
    {
        string starters = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "starters");

        return Directory.Exists(starters)
            ? Directory.EnumerateDirectories(starters)
                .Select(starter => Path.Combine(starter, "src", "Presentation"))
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories)
                .Where(host => File.Exists(Path.Combine(host, "Program.cs")))
            : [];
    }

    private static class Widgets
    {
        internal sealed record ArchiveCommand;
    }

    private static class Gadgets
    {
        internal sealed record ArchiveCommand;
    }
    //#endif

    private static List<string> HandlerCountFailures(IEnumerable<Type> messages, IEnumerable<(Type Handler, Type Message)> handled)
    {
        var counts = handled
            .GroupBy(pair => pair.Message)
            .ToDictionary(group => group.Key, group => group.Count());

        return
        [
            .. messages
                .Select(message => (Message: message, Count: counts.GetValueOrDefault(message)))
                .Where(entry => entry.Count != 1)
                .Select(entry => $"{entry.Message.FullName} has {entry.Count} handlers")
                .Order(StringComparer.Ordinal),
        ];
    }

    private sealed record Unhandled;

    private sealed record HandledOnce;

    private sealed record HandledTwice;

    private sealed class FirstHandler;

    private sealed class SecondHandler;
}
