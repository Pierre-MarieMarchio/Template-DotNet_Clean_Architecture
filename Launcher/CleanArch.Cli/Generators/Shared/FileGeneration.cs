namespace CleanArch.Cli.Generators.Shared;

internal abstract class FileGeneration(GenerationContext context, string features, string? manifest) : Generation(context)
{
    private readonly List<(string Path, byte[] Original, byte[] Bytes)> _replaced = [];

    protected IReadOnlyList<Emission> Emissions { get; private set; } = [];

    protected TextEdits.Editor Editor { get; private set; } = new(context.Root);

    protected override IReadOnlyList<string> EditFailures => Editor.Failures;

    protected override IReadOnlyList<(string Path, byte[] Bytes)> Created => [.. Emissions.Select(emission => (emission.Path, emission.Bytes()))];

    protected override IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> Replaced => _replaced;

    protected abstract IReadOnlyList<Emission> Plan();

    protected abstract TextEdits.Editor Edit(List<string> problems);

    protected override bool Draft(List<string> problems)
    {
        Emissions = Plan();

        if (ExistingFiles(Root, Emissions) is { } existing)
        {
            problems.Add(existing);

            return false;
        }

        Editor = Edit(problems);

        return problems.Count == 0;
    }

    protected override bool Record(List<string> problems)
    {
        _replaced.AddRange(TextEdits.Replacements(Editor));

        if (manifest is null)
        {
            return true;
        }

        try
        {
            _replaced.Add(ManifestWith(Root, features, Emissions, Editor));

            return true;
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(
                $"{exception.Message} {manifest} records each edit for remove-feature to undo with the feature: "
                + "give the file the ending this project writes (format-fix), or make this edit by hand, then run this again.");

            return false;
        }
    }
}
