using System.Text.RegularExpressions;
using CleanArch.Create.Setup;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Prompts;

namespace CleanArch.Create.Commands.NewProject;

public sealed record NewAnswers(string Name, string Output, Dictionary<string, string> Options, IReadOnlyList<string> Setup);

internal static partial class NewQuestions
{
    private const string _nameKey = "--name";
    private const string _outputKey = "--output";
    private const string _setupKey = "--setup";
    private const char _setupSeparator = '|';

    private static readonly IReadOnlyDictionary<string, string> _nothing = new Dictionary<string, string>();

    public static NewAnswers Ask(Session session, TemplateManifest manifest, IReadOnlyDictionary<string, Condition> askedWhen)
    {
        Dictionary<string, string> answers = AddedHosts.WithTheHostsAdded(
            manifest,
            session.Questionnaire.Answer(manifest, session.Parsed.TemplateOptions, _nothing, askedWhen, Before(session, manifest), After(session, manifest)));
        return new NewAnswers(answers.GetValueOrDefault(_nameKey)!, answers.GetValueOrDefault(_outputKey)!, answers, Steps(answers.GetValueOrDefault(_setupKey)));
    }

    private static Question[] Before(Session session, TemplateManifest manifest)
    {
        Questionnaire questionnaire = session.Questionnaire;
        Arguments parsed = session.Parsed;
        return
        [
            new(_nameKey, (_, remembered) => questionnaire.Text("--name", "Project name", parsed.Name, remembered ?? manifest.DefaultName, name => ProjectNameProblem(manifest, name))),
            new(_outputKey, (answers, _) =>
            {
                string? named = answers.GetValueOrDefault(_nameKey);
                if (parsed.Output is not null || !questionnaire.Asks || named is null)
                {
                    return Reply.Passed(parsed.Output ?? named);
                }

                return questionnaire.Text("--output", "Output folder", null, "./" + named, folder => OutputProblem(session.Machine.WorkingDirectory, folder));
            }),
        ];
    }

    private static Question[] After(Session session, TemplateManifest manifest) =>
    [
        AddedHosts.Question(session.Conversation, manifest),
        SetupQuestion(session),
        ItemAdditions.Reviewed(
            session.Conversation,
            given =>
            [
                KeyValuePair.Create("Project name", given.GetValueOrDefault(_nameKey) ?? string.Empty),
                KeyValuePair.Create("Output folder", given.GetValueOrDefault(_outputKey) ?? string.Empty),
                .. Questionnaire.Recap(manifest, AddedHosts.WithTheHostsAdded(manifest, given)),
                KeyValuePair.Create("Setup steps", Recalled(Steps(given.GetValueOrDefault(_setupKey)))),
            ],
            given => $"Generate {given.GetValueOrDefault(_nameKey)}"),
    ];

    private static Question SetupQuestion(Session session) =>
        new(_setupKey, (answers, remembered) =>
        {
            string[] every = [.. ProjectSetup.Steps.Select(step => step.Title)];
            if (session.Parsed.Setup)
            {
                return Reply.Passed(string.Join(_setupSeparator, every));
            }

            if (!session.Questionnaire.Asks)
            {
                return Reply.Passed(string.Empty);
            }

            IReadOnlyList<string>? chosen = session.Conversation.ChooseMany(
                "Set up the project now?",
                string.Empty,
                [.. ProjectSetup.Steps.Select(step => new OptionChoice(step.Title, step == ProjectSetup.Bootstrap ? BootstrapDescription(session, answers) : step.Description))],
                remembered is null ? every : Steps(remembered),
                atLeastOne: false);
            return chosen is null ? Reply.Backward : Reply.Answer(string.Join(_setupSeparator, chosen));
        });

    private static string BootstrapDescription(Session session, IReadOnlyDictionary<string, string> answers)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.Project);
        return BootstrapStep.DescriptionFor(symbol => Condition.IsTruthy(manifest.Resolve(symbol, answers)));
    }

    private static string[] Steps(string? chosen) => chosen?.Split(_setupSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static string Recalled(string[] setup) => setup.Length == 0 ? "Nothing" : string.Join(", ", setup);

    private static string? OutputProblem(string workingDirectory, string folder)
    {
        string target = Path.GetFullPath(folder, workingDirectory);
        return Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() ? "that folder already has files in it." : null;
    }

    private static string? ProjectNameProblem(TemplateManifest manifest, string name) =>
        ProjectName().IsMatch(name) ? ItemAdditions.NameRefusal(manifest, _nothing, name) : "use the letters A to Z, digits and dots, with each part starting with a letter, like Acme.Shop.";

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$")]
    private static partial Regex ProjectName();
}
