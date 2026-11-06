using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddUseCase;

internal sealed class UseCaseGeneration(UseCaseSpec spec, GenerationContext context) : Generation(context)
{
    private readonly TextEdits.Editor _editor = new(context.Root);

    private readonly List<(string Path, byte[] Original, byte[] Bytes)> _replaced = [];

    private List<Emission> _plan = [];

    private List<Emission> _contracts = [];

    private string? _skipped;

    protected override string Usage => NewUseCase.Usage;

    protected override bool StopsAtDryRun => true;

    protected override IReadOnlyList<string> EditFailures => _editor.Failures;

    protected override IReadOnlyList<(string Path, byte[] Bytes)> Created => [.. _plan.Concat(_contracts).Select(emission => (emission.Path, emission.Bytes()))];

    protected override IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> Replaced => _replaced;

    protected override bool Draft(List<string> problems)
    {
        _plan = UseCasePlan.Plan(spec, Root, out _skipped);
        _contracts = UseCaseHttp.Http(_editor, Root, spec);

        if (ExistingFiles(Root, _plan.Concat(_contracts)) is { } existing)
        {
            problems.Add(existing);
        }

        return problems.Count == 0;
    }

    protected override int RefuseEdits(IReadOnlyList<string> failures) =>
        RefuseAfterReport(failures, "Nothing was written: every edit is made or none is. Put back the line the failed edit is placed beside, or make the edit above by hand.");

    protected override bool Record(List<string> problems)
    {
        _replaced.AddRange(TextEdits.Replacements(_editor));

        if (spec.Manifest is not null)
        {
            _replaced.Add(ManifestWith(Root, spec.Features, [.. _plan, .. _contracts], _editor));
        }

        return true;
    }

    protected override void Report(ReportMode mode) => UseCaseReport.Write(Output, Root, spec, _plan, _skipped, mode);

    protected override int Finish() =>
        _contracts.Count == 0 && _editor.Applied.Count == 0
        || TextEdits.RunAll(TextEdits.ContractSteps(Root, FolderOf(spec.Prefix, "Presentation.Client"), built: false), Context.Executor.Run, Output, Error, "the use case's files, its action, its contracts and its route's row")
            ? Succeeded
            : Failed;
}
