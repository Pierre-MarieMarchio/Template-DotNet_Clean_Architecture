namespace CleanArch.Cli.Generators.Shared;

internal abstract class Generation(GenerationContext context)
{
    protected const int Succeeded = 0;

    protected const int Failed = 1;

    protected GenerationContext Context => context;

    protected string Root => context.Root;

    protected TextWriter Output => context.Output;

    protected TextWriter Error => context.Error;

    protected abstract string Usage { get; }

    protected virtual bool StopsAtDryRun => false;

    protected virtual string EditAdvice => "Fix that file, or make this edit by hand.";

    internal int Generate()
    {
        if (Resume() is { } resumed)
        {
            return resumed;
        }

        var problems = new List<string>();

        if (!Draft(problems))
        {
            return Refuse(problems);
        }

        if (EditFailures.Count > 0)
        {
            return RefuseEdits(EditFailures);
        }

        if (context.DryRun && StopsAtDryRun)
        {
            return StopAtDryRun();
        }

        if (!Record(problems))
        {
            return Refuse(problems);
        }

        if (!context.DryRun && Write() is { } refused)
        {
            return refused;
        }

        Report(context.DryRun ? ReportMode.DryRun : ReportMode.Applied);

        return context.DryRun ? Succeeded : Finish();
    }

    protected abstract IReadOnlyList<string> EditFailures { get; }

    protected abstract IReadOnlyList<(string Path, byte[] Bytes)> Created { get; }

    protected abstract IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> Replaced { get; }

    protected virtual int? Resume() => null;

    protected abstract bool Draft(List<string> problems);

    protected virtual bool Record(List<string> problems) => true;

    protected virtual string? Preconditions() => null;

    protected abstract void Report(ReportMode mode);

    protected virtual int Finish() => Succeeded;

    protected int Refuse(IEnumerable<string> problems) => Refusal.Write(Error, problems, Usage);

    protected virtual int RefuseEdits(IReadOnlyList<string> failures) => Refuse(failures.Select(failure => $"{failure} {EditAdvice}"));

    protected static string? ExistingFiles(string root, IEnumerable<Emission> emissions)
    {
        string[] existing = [.. emissions.Select(emission => emission.Path).Where(path => File.Exists(Path.Combine(root, path)))];

        return existing.Length == 0
            ? null
            : $"{existing.Length} of the files to write already exist, and none is ever overwritten:\n" + string.Join("\n", existing.Select(path => $"  {path}"));
    }

    protected static (string Path, byte[] Original, byte[] Bytes) ManifestWith(string root, string features, IEnumerable<Emission> emissions, TextEdits.Editor editor)
    {
        var manifest = FeatureManifest.Read(root, features, out _)!;

        foreach (var emission in emissions)
        {
            manifest.Record(emission.Path, emission.Bytes());
        }

        manifest.Record(editor);

        return manifest.Replacement;
    }

    private int StopAtDryRun()
    {
        Report(ReportMode.DryRun);
        Output.WriteLine();
        Output.WriteLine("--dry-run: nothing was written and nothing was run.");

        return Succeeded;
    }

    private int? Write()
    {
        if (Preconditions() is { } refusal)
        {
            Error.WriteLine($"error: {refusal}");
            Error.WriteLine("Nothing was written.");

            return Failed;
        }

        try
        {
            TextEdits.Commit(Root, Created, Replaced);

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

            return Failed;
        }
    }
}
