using System.CommandLine;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddSetting;

internal static class AddSettingCommand
{
    internal static Command Build(CommandContext context) => Build(context.Project, GeneratorCommand.Executor);

    internal static Command Build(LocatedProject? project, Func<ParseResult, IExecutor> executor)
    {
        var setting = new Command("setting", "Write a feature's options class and its validator, register them and ship the section in each host's appsettings.json and the configuration reference.");
        Argument<string> features = new("Features") { Description = "The folder of an existing feature, composed by at least one host, e.g. Invoices." };
        Argument<string> section = new("Section") { Description = "The configuration section, PascalCase, e.g. InvoiceReminders; its options class is <Section>Options." };
        Option<string> keys = GeneratorCommand.RequiredText("--keys", "One entry per key, Name:type[(min..max)][=default], of int, long, duration, bool, string and secret, e.g. \"MaxItems:int(1..100)=10\".");
        Option<bool> dryRun = GeneratorCommand.DryRun("Print what would be written, and write nothing.");
        setting.Arguments.Add(features);
        setting.Arguments.Add(section);
        setting.Options.Add(keys);
        setting.Options.Add(dryRun);
        GeneratorCommand.SetGeneration(
            setting,
            project,
            parseResult => parseResult.GetValue(dryRun),
            executor,
            (parseResult, context) => NewSetting.Run(new AddSettingRequest(parseResult.GetRequiredValue(features), parseResult.GetRequiredValue(section), parseResult.GetRequiredValue(keys)), context));

        return setting;
    }
}
