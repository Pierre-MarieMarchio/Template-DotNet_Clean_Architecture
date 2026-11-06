using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddSetting;

internal sealed class SettingGeneration(SettingSpec spec, GenerationContext context) : FileGeneration(context, spec.Features, spec.Manifest)
{
    private List<string> _hosts = [];

    protected override string Usage => NewSetting.Usage;

    protected override string EditAdvice => "Fix that file, or make this edit by hand and leave the setting to the steps of docs/guides/adding-a-setting.md.";

    protected override IReadOnlyList<Emission> Plan() => SettingPlan.Emissions(spec, Root);

    protected override TextEdits.Editor Edit(List<string> problems)
    {
        (var editor, _hosts) = SettingEdits.Edits(spec, Root, problems);

        return editor;
    }

    protected override void Report(ReportMode mode) =>
        SettingReport.Write(Output, spec, Emissions, Editor.Changed.Select(sheet => sheet.Path), _hosts, mode == ReportMode.DryRun);
}
