using System.Collections.ObjectModel;
using CleanArch.Create.Setup;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Prompts;

namespace CleanArch.Create.Commands.NewProject;

public sealed record NewAnswers(string Name, string Output, string? ExistingFiles, Dictionary<string, string> Options, IReadOnlyList<string> Setup);

internal static class NewQuestions
{
    private const string _nameKey = "--name";
    private const string _outputKey = "--output";
    private const string _existingFilesKey = "--existing-files";
    private const string _currentFolder = ".";
    private const string _setupKey = "--setup";
    private const char _setupSeparator = '|';

    public static NewAnswers Ask(Session session, TemplateManifest manifest, IReadOnlyDictionary<string, Condition> askedWhen)
    {
        Dictionary<string, string> answers = AddedHosts.WithTheHostsAdded(
            manifest,
            session.Questionnaire.Answer(manifest, session.Parsed.TemplateOptions, ReadOnlyDictionary<string, string>.Empty, askedWhen, Before(session, manifest), After(session, manifest)));
        return new NewAnswers(answers.GetValueOrDefault(_nameKey)!, answers.GetValueOrDefault(_outputKey)!, answers.GetValueOrDefault(_existingFilesKey), answers, Steps(answers.GetValueOrDefault(_setupKey)));
    }

    private static Question[] Before(Session session, TemplateManifest manifest)
    {
        Arguments parsed = session.Parsed;
        string? proposed = ProjectNaming.Proposed(manifest, session.Machine.WorkingDirectory);
        return
        [
            new(_nameKey, (_, remembered) => session.Questionnaire.Text("--name", "Project name", parsed.Name, remembered ?? proposed, name => ProjectNaming.Problem(manifest, name))),
            new(_outputKey, (answers, _) => Reply.Passed(parsed.Output ?? parsed.Name ?? Folder(answers.GetValueOrDefault(_nameKey), proposed))),
            ExistingFilesQuestion(session),
        ];
    }

    private static string? Folder(string? named, string? proposed)
    {
        if (named is null)
        {
            return null;
        }

        return named == proposed ? _currentFolder : "./" + named;
    }

    private static Question ExistingFilesQuestion(Session session) =>
        new(_existingFilesKey, (answers, _) =>
        {
            string? output = answers.GetValueOrDefault(_outputKey);
            string target = Path.GetFullPath(output ?? _currentFolder, session.Machine.WorkingDirectory);
            if (output is null || !ExistingFiles.In(target))
            {
                return Reply.Passed(null);
            }

            if (session.Parsed.Overwrite)
            {
                return Reply.Passed(ExistingFiles.Remove);
            }

            if (!session.Questionnaire.Asks)
            {
                throw new LauncherException(ExistingFiles.Refusal(target));
            }

            string? chosen = session.Conversation.Choose($"{target} already has files in it", string.Empty, ExistingFiles.Choices, ExistingFiles.Cancel);
            if (chosen == ExistingFiles.Cancel)
            {
                throw new LauncherException(ItemAdditions.Stopped);
            }

            return chosen is null ? Reply.Backward : Reply.Answer(chosen);
        });

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
                .. ExistingFilesRow(given),
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

    private static IEnumerable<KeyValuePair<string, string>> ExistingFilesRow(IReadOnlyDictionary<string, string> given) =>
        given.TryGetValue(_existingFilesKey, out string? chosen) ? [KeyValuePair.Create("Existing files", chosen)] : [];
}
