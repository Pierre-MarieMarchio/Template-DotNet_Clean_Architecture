using System.CommandLine;
using CleanArch.Cli.Generators.AddEvent;
using CleanArch.Cli.Generators.AddFeature;
using CleanArch.Cli.Generators.AddField;
using CleanArch.Cli.Generators.AddSetting;
using CleanArch.Cli.Generators.AddUseCase;

namespace CleanArch.Cli;

internal static class FeatureAdditions
{
    public static IEnumerable<Command> Commands(CommandContext context)
    {
        Command[] generators =
        [
            FeatureCommand(context),
            FieldCommand(context),
            UseCaseCommand(context),
            SettingCommand(context),
            EventCommand(context),
        ];
        foreach (Command generator in generators)
        {
            if (Refusal(context, generator.Name) is { } refusal)
            {
                CleanArchCommand.Refuse(generator, refusal);
            }
        }

        return generators;
    }

    internal static string? Refusal(CommandContext context, string generator) =>
        context.Project?.File.Options.Auth == "none" && generator is "feature" or "field"
            ? $"This project has no add {generator} command: its {ProjectLocator.FileName} records auth none, so it authenticates nobody, "
                + "and every feature add feature writes (--access owner or shared), and so every feature add field extends, needs an "
                + "authenticated caller. Add an authentication scheme and an ICurrentUser that reads it before scaffolding a feature, "
                + "or write a public feature by hand."
            : null;

    private static Command FeatureCommand(CommandContext context)
    {
        var feature = new Command("feature", "Write a feature in every layer, its manifest and its page, compose it, then build, add its migration and rewrite the OpenAPI reference.");
        Argument<string> features = new("Features") { Description = "The feature's folder, PascalCase and plural, e.g. Widgets." };
        Argument<string> type = new("Feature") { Description = "The feature's type, PascalCase and singular, e.g. Widget." };
        Option<string?> access = GeneratorCommand.Text("--access", "owner: every operation is scoped to the caller's id. shared: every authenticated caller sees the same records.", required: true);
        Option<string?> deletion = GeneratorCommand.Text("--deletion", "With --access owner, required: delete removes the rows of an account deleted, anonymise rewrites what identifies its owner.");
        Option<string?> operations = GeneratorCommand.Text("--operations", "Of get, list, create, update and delete, comma separated; all five by default. create needs get.");
        Option<string?> model = GeneratorCommand.Text("--model", "record (default): a service over a store. aggregate: one use case per operation over a domain aggregate.");
        Option<string?> concurrency = GeneratorCommand.Text("--concurrency", "none (default): last write wins. etag: a persisted version, ETag and If-Match; aggregate needs it.");
        Option<string?> fields = GeneratorCommand.Text("--fields", "The fields, Name:type, comma separated, e.g. \"Title:string(120), Notes:text?, Budget:decimal>=0\"; Name and Description by default.");
        Option<bool> dryRun = GeneratorCommand.DryRun("Print the files and the edits, and write and run nothing.");
        feature.Arguments.Add(features);
        feature.Arguments.Add(type);
        feature.Options.Add(access);
        feature.Options.Add(deletion);
        feature.Options.Add(operations);
        feature.Options.Add(model);
        feature.Options.Add(concurrency);
        feature.Options.Add(fields);
        feature.Options.Add(dryRun);
        if (context.Project is { } project)
        {
            feature.SetAction(parseResult => NewFeature.Run(
                [
                    parseResult.GetRequiredValue(features),
                    parseResult.GetRequiredValue(type),
                    .. GeneratorCommand.Forwarded(parseResult, access, deletion, operations, model, concurrency, fields),
                    .. GeneratorCommand.Flag(parseResult, dryRun),
                    .. GeneratorCommand.Root(project),
                ],
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                GeneratorCommand.Executor(parseResult),
                GeneratorCommand.Version));
        }

        return feature;
    }

    private static Command FieldCommand(CommandContext context)
    {
        var field = new Command("field", "Add fields to a feature add feature wrote with --fields, then build and add their migration with each default.");
        Argument<string> features = new("Features") { Description = "A feature add feature wrote with --fields, which its manifest records." };
        Argument<string> declarations = new("fields") { Description = "The fields, in the grammar of --fields; one that cannot be null takes the default the rows stored receive after '=', e.g. \"Code:string(20)='none', Notes:text?\"." };
        Option<bool> dryRun = GeneratorCommand.DryRun("Print what changes, and write and run nothing.");
        field.Arguments.Add(features);
        field.Arguments.Add(declarations);
        field.Options.Add(dryRun);
        if (context.Project is { } project)
        {
            field.SetAction(parseResult => NewField.Run(
                [
                    parseResult.GetRequiredValue(features),
                    parseResult.GetRequiredValue(declarations),
                    .. GeneratorCommand.Flag(parseResult, dryRun),
                    .. GeneratorCommand.Root(project),
                ],
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                GeneratorCommand.Executor(parseResult)));
        }

        return field;
    }

    private static Command UseCaseCommand(CommandContext context)
    {
        var useCase = new Command("use-case", "Add one operation to a feature of the rich shape: its message, handler, validator and tests, and its action where the API serves the feature.");
        Argument<string> features = new("Features") { Description = "The folder of an existing feature of the rich shape, e.g. Invoices." };
        Argument<string> name = new("UseCase") { Description = "The operation, PascalCase, a verb and its subject, e.g. ApproveInvoice." };
        Option<string?> kind = GeneratorCommand.Text("--kind", "command: changes state and answers a Result. query: reads, and answers a Result of a DTO named for it.", required: true);
        Option<string?> input = GeneratorCommand.Text("--input", "What the message carries: id (default), none, or \"[id, ]Name:Type, Other:Type\".");
        Option<bool> dryRun = GeneratorCommand.DryRun("Print the files and the edit, and write and run nothing.");
        useCase.Arguments.Add(features);
        useCase.Arguments.Add(name);
        useCase.Options.Add(kind);
        useCase.Options.Add(input);
        useCase.Options.Add(dryRun);
        if (context.Project is { } project)
        {
            useCase.SetAction(parseResult => NewUseCase.Run(
                [
                    parseResult.GetRequiredValue(features),
                    parseResult.GetRequiredValue(name),
                    .. GeneratorCommand.Forwarded(parseResult, kind, input),
                    .. GeneratorCommand.Flag(parseResult, dryRun),
                    .. GeneratorCommand.Root(project),
                ],
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                GeneratorCommand.Executor(parseResult)));
        }

        return useCase;
    }

    private static Command SettingCommand(CommandContext context)
    {
        var setting = new Command("setting", "Write a feature's options class and its validator, register them and ship the section in each host's appsettings.json and the configuration reference.");
        Argument<string> features = new("Features") { Description = "The folder of an existing feature, composed by at least one host, e.g. Invoices." };
        Argument<string> section = new("Section") { Description = "The configuration section, PascalCase, e.g. InvoiceReminders; its options class is <Section>Options." };
        Option<string?> keys = GeneratorCommand.Text("--keys", "One entry per key, Name:type[(min..max)][=default], of int, long, duration, bool, string and secret, e.g. \"MaxItems:int(1..100)=10\".", required: true);
        Option<bool> dryRun = GeneratorCommand.DryRun("Print what would be written, and write nothing.");
        setting.Arguments.Add(features);
        setting.Arguments.Add(section);
        setting.Options.Add(keys);
        setting.Options.Add(dryRun);
        if (context.Project is { } project)
        {
            setting.SetAction(parseResult => NewSetting.Run(
                [
                    parseResult.GetRequiredValue(features),
                    parseResult.GetRequiredValue(section),
                    .. GeneratorCommand.Forwarded(parseResult, keys),
                    .. GeneratorCommand.Flag(parseResult, dryRun),
                    .. GeneratorCommand.Root(project),
                ],
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error));
        }

        return setting;
    }

    private static Command EventCommand(CommandContext context)
    {
        var domainEvent = new Command("event", "Write a domain event a feature of the rich shape raises, with its consumer or its place on the list of events nobody consumes, and their tests.");
        Argument<string> features = new("Features") { Description = "The folder of an existing feature of the rich shape, which raises the event, e.g. Invoices." };
        Argument<string> name = new("Event") { Description = "PascalCase, past tense, ending in DomainEvent, e.g. InvoicePaidDomainEvent." };
        Option<string?> consumer = GeneratorCommand.Text("--consumer", "The feature that reacts, and what it does, <Features>:<Reaction>, e.g. Reminders:CancelReminders.");
        Option<string?> unconsumed = GeneratorCommand.Text("--unconsumed", "Instead of a consumer, the reason nothing listens; one of the two is required.");
        Option<string?> fields = GeneratorCommand.Text("--fields", "What the event carries besides OccurredOn, Name:Type, comma separated; the id of the feature's aggregate by default.");
        Option<bool> dryRun = GeneratorCommand.DryRun("Print what would be written, and write nothing.");
        domainEvent.Arguments.Add(features);
        domainEvent.Arguments.Add(name);
        domainEvent.Options.Add(consumer);
        domainEvent.Options.Add(unconsumed);
        domainEvent.Options.Add(fields);
        domainEvent.Options.Add(dryRun);
        if (context.Project is { } project)
        {
            domainEvent.SetAction(parseResult => NewEvent.Run(
                [
                    parseResult.GetRequiredValue(features),
                    parseResult.GetRequiredValue(name),
                    .. GeneratorCommand.Forwarded(parseResult, consumer, unconsumed, fields),
                    .. GeneratorCommand.Flag(parseResult, dryRun),
                    .. GeneratorCommand.Root(project),
                ],
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error));
        }

        return domainEvent;
    }
}
