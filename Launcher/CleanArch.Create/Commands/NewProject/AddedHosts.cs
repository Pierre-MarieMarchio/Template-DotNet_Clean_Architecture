using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Prompts;

namespace CleanArch.Create.Commands.NewProject;

public static class AddedHosts
{
    public static readonly IReadOnlyList<ApiCaller> ApiCallers =
    [
        new("the web app", Condition.Parse("(anyWeb)")),
        new("the desktop client", Condition.Parse("(presentationAvalonia || presentationWpf)")),
        new("the MCP server under local accounts", Condition.Parse("(presentationMcp && localAuth)")),
    ];

    private const string _hostsAddedKey = "--hosts-added";
    private const string _presentationSymbol = "presentation";
    private const string _apiChoice = "api";
    private const string _workerChoice = "worker";

    public static IReadOnlyList<string> CallersOfTheApiAdded(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers) =>
        answers.TryGetValue(_presentationSymbol, out string? chosen) && !TemplateOption.Values(chosen).Contains(_apiChoice)
            ? [.. ApiCallers.Where(caller => manifest.Holds(caller.When, answers)).Select(caller => caller.Who)]
            : [];

    public static bool WorkerAdded(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers) =>
        answers.TryGetValue(_presentationSymbol, out string? chosen)
            && !TemplateOption.Values(chosen).Any(value => value is _apiChoice or _workerChoice)
            && !ApiCallers.Any(caller => manifest.Holds(caller.When, answers));

    public static Dictionary<string, string> WithTheHostsAdded(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers)
    {
        var completed = new Dictionary<string, string>(answers, StringComparer.Ordinal);
        if (CallersOfTheApiAdded(manifest, answers).Count > 0)
        {
            completed[_presentationSymbol] = PresentationOption(manifest).Joined([.. TemplateOption.Values(answers[_presentationSymbol]), _apiChoice]);
        }
        else if (WorkerAdded(manifest, answers))
        {
            completed[_presentationSymbol] = PresentationOption(manifest).Joined([.. TemplateOption.Values(answers[_presentationSymbol]), _workerChoice]);
        }

        return completed;
    }

    public static Question Question(Conversation conversation, TemplateManifest manifest) =>
        new(_hostsAddedKey, (answers, _) =>
        {
            string? why = CallersOfTheApiAdded(manifest, answers) is { Count: > 0 } callers
                ? $"the API added for you: {string.Join(" and ", callers)} {(callers.Count == 1 ? "calls" : "call")} it"
                : WorkerAdded(manifest, answers) ? "the worker added for you: the jobs run in it" : null;
            if (why is not null)
            {
                TemplateOption presentation = PresentationOption(manifest);
                conversation.Answered(presentation.Label, Questionnaire.Shown(presentation, WithTheHostsAdded(manifest, answers)[_presentationSymbol]), why);
            }

            return Reply.Passed(null);
        });

    private static TemplateOption PresentationOption(TemplateManifest manifest) =>
        manifest.Options.Single(option => option.Symbol == _presentationSymbol);

    public sealed record ApiCaller(string Who, Condition When);
}
