using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.CSharpNames;
using static CleanArch.Cli.Generators.Shared.FeatureCode;

namespace CleanArch.Cli.Generators.AddFeature;

internal static class FeatureReport
{
    internal const string _removalRefusal = "remove-feature refuses to remove this feature while another owner feature still uses these files.";

    internal static void Report(TextWriter output, string root, Spec spec, List<Emission> plan, string manifest, List<string> skipped, ReportMode mode)
    {
        string p = spec.Prefix;
        string f = spec.Features;
        string a = spec.Type;
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        string verb = spec.Deletion == "anonymise" ? "Anonymise" : "Delete";
        var flags = Flags(spec);
        string[] hosts = ComposingHosts(root, spec);

        output.WriteLine(
            (mode == ReportMode.Applied ? "Wrote" : "Would write")
            + $" {plan.Count} files for {f}/{a} "
            + $"(access {spec.Access}, model {spec.Model}, operations {string.Join(',', spec.Operations)}, "
            + $"concurrency {spec.Concurrency}"
            + (owner ? $", deletion {spec.Deletion}" : string.Empty)
            + "):");

        foreach (var emission in plan)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();
        output.WriteLine(mode == ReportMode.Applied
            ? $"Manifest: {manifest} (the options, the SHA-256 of each file above, and each edit below with the line it is placed beside)."
            : $"Manifest: {manifest} (the options, the SHA-256 of each file above, and each edit below with the line it is placed beside), not written.");

        foreach (string project in skipped)
        {
            output.WriteLine($"Not written: '{project}' is not in this tree, so its tests were skipped.");
        }

        output.WriteLine();
        output.WriteLine(mode switch
        {
            ReportMode.Applied => "The feature composes through these edits, each placed beside a line it found and recorded in the manifest.",
            ReportMode.DryRun => "A run without --dry-run makes these edits, each placed beside a line it finds, or none when a line is missing.",
            _ => "None of these edits was made: the solution does not compile until edit 1 is made, nor the feature compose until all are.",
        });
        output.WriteLine(spec.Reaction is null
            ? "Six edits, each somewhere an absence should be visible:"
            : "Eleven edits: six like every feature, and five more because this project was");
        output.WriteLine(spec.Reaction is null
            ? string.Empty
            : "generated with no feature that owns user data, so its account-deleted reaction ships with this one.");

        if (spec.Reaction is not null)
        {
            output.WriteLine(_removalRefusal);
        }

        output.WriteLine();
        output.WriteLine($"  1. {ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence")}/Common/Contexts/AppDbContext.cs");
        output.WriteLine($"       public const string {f}Schema = \"{Snake(f)}\";");
        output.WriteLine($"       internal DbSet<{a}Record> {f} => Set<{a}Record>();");
        output.WriteLine($"       modelBuilder.ApplyConfiguration(new {a}RecordConfiguration());");
        output.WriteLine($"     with the usings of {p}.Infrastructure.Persistence.Features.{f}.Rows.Configurations and .Rows.Models");
        output.WriteLine();
        output.WriteLine($"  2. {ProjectOf(spec, "Src/Application", "Application")}/ApplicationModule.cs: an Add{f}() beside the others");

        if (record || flags.Contains("change"))
        {
            output.WriteLine($"       services.AddFeature(\"{f}\");   // its validators, found by folder, and the use-case pipeline");
            output.WriteLine($"       services.AddScoped<I{a}Service, {a}Service>();");
        }
        else
        {
            output.WriteLine($"       services.AddFeature(\"{f}\");   // its validators, found by folder, and the use-case pipeline");
        }

        if (owner)
        {
            output.WriteLine($"       services.AddDomainEventConsumer<UserAccountDeleted, {verb}{a}OnAccountDeletedConsumer>();");
            output.WriteLine($"       services.AddScoped<IDeletedAccountDataPolicy, {verb}{a}OnAccountDeletedConsumer>();");
            output.WriteLine("     // the declared fate of this feature's data once the account that owned it is gone, applied");
            output.WriteLine("     // by the consumer when the fact arrives and by the sweep when it never does");
        }

        output.WriteLine();
        output.WriteLine($"  3. {ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence")}/PersistenceModule.cs: an Add{f}Feature(services),");
        output.WriteLine("     called from AddPersistenceModule, registering the adapters of this feature's ports:");

        if (record)
        {
            if (flags.Contains("stage"))
            {
                output.WriteLine($"       services.TryAddScoped<I{a}Store, {a}Store>();");
            }

            if (flags.Contains("rq"))
            {
                output.WriteLine($"       services.TryAddScoped<I{a}Queries, {a}Queries>();");
            }
        }
        else
        {
            output.WriteLine($"       services.TryAddSingleton<I{a}Mapper, {a}Mapper>();");
            output.WriteLine($"       AddScopedTracker<{a}Tracker, I{a}Tracker>(services);");
            output.WriteLine($"       services.TryAddScoped<I{a}Repository, {a}Repository>();");

            if (flags.Contains("rq"))
            {
                output.WriteLine($"       services.TryAddScoped<I{a}Queries, {a}Queries>();");
            }
        }

        output.WriteLine();
        output.WriteLine($"  4. Src/Presentation/{hosts[0]}/Program.cs: builder.Services.Add{f}(); beside the other features,");
        output.WriteLine($"     with using {p}.Application; if the file has none yet");

        foreach (string host in hosts.Skip(1))
        {
            output.WriteLine($"     and Src/Presentation/{host}/Program.cs the same, since that host composes the features itself");
        }
        output.WriteLine();
        output.WriteLine($"  5. {ProjectOf(spec, "Tests/Architecture", "Architecture.Tests")}/Composition/HostComposition.cs: services.Add{f}(); in each");
        output.WriteLine("     composition that offers the feature, so the container tests resolve what it registers,");
        output.WriteLine($"     with using {p}.Application; if the file has none yet");
        output.WriteLine();

        string moduleTests = $"{ProjectOf(spec, "Tests/Application", "Application.UnitTests")}/ApplicationModuleTests.cs";

        if (File.Exists(Path.Combine(root, moduleTests)))
        {
            output.WriteLine($"  6. {moduleTests}: services.Add{f}(); in ComposeEveryFeature,");
            output.WriteLine("     so the registration facts read the feature's use cases and validators");
        }
        else
        {
            output.WriteLine($"  6. Nothing to edit: {moduleTests} is only generated with a feature,");
            output.WriteLine("     and this project had none. Step 5 is what makes the container tests resolve the feature.");
        }

        if (owner && spec.Reaction is { } adapter)
        {
            string directory = adapter == "Auth" ? "AccountUserAccountDirectory" : "ExternalIdentityUserAccountDirectory";
            string jobsHost = JobsHost(root, p);
            string composition = !spec.HasApi
                ? HostComposition(root, hosts[0], adapter)
                : adapter == "Auth"
                    ? $"{ProjectOf(spec, "Src/Presentation", "Api")}/Features/Auth/Composition/AuthComposition.cs"
                    : $"{ProjectOf(spec, "Src/Presentation", "Api")}/Features/ExternalIdentity/Composition/ExternalAuthenticationComposition.cs";

            output.WriteLine();
            output.WriteLine($"  7. {ProjectOf(spec, "Src/Application", "Application.Core")}/ApplicationCoreModule.cs: the AddDeletedAccountSweep()");
            output.WriteLine("     method before AddUseCasePipeline, composing the use-case pipeline as AddPurgeExpiredIdempotencyKeys does:");

            foreach (string line in FeatureEdits.SweepMethod())
            {
                output.WriteLine(line.Length == 0 ? string.Empty : "     " + line);
            }
            output.WriteLine();
            output.WriteLine($"  8. Src/Presentation/{jobsHost}/Program.cs: builder.Services.AddDeletedAccountSweep(); beside");
            output.WriteLine("     builder.Services.AddPurgeExpiredIdempotencyKeys();");
            output.WriteLine();
            output.WriteLine($"  9. Src/Presentation/{jobsHost}/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs:");
            output.WriteLine("     services.AddDeletedAccountSweepJobs(configuration); after services.AddMaintenanceJobs(configuration);");
            output.WriteLine();
            output.WriteLine($" 10. {composition}: services.AddScoped<IUserAccountDirectory, {directory}>();");
            output.WriteLine("     with the using of Application.Core's ports; the directory shares the composition's namespace");

            foreach (string host in hosts.Skip(1))
            {
                output.WriteLine($"     and in {HostComposition(root, host, adapter)}, beside the directory written for that host");
            }
            output.WriteLine();
            output.WriteLine($" 11. {ProjectOf(spec, "Tests/Architecture", "Architecture.Tests")}/Composition/HostComposition.cs: services.AddDeletedAccountSweep();");
            output.WriteLine("     after services.AddPurgeExpiredIdempotencyKeys(); in each composition, and in AddHostSuppliedAdapters");
            output.WriteLine(spec.HasApi
                ? "     and AddBackgroundOnlyAdapters the double that stands in for the directory the API composes:"
                : "     and AddBackgroundOnlyAdapters the double that stands in for the directory the hosts compose:");
            output.WriteLine("       services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();");
            output.WriteLine("     declared beside the other doubles, or the container tests cannot resolve the feature's consumer:");
            foreach (string line in FeatureEdits.DirectoryDouble())
            {
                output.WriteLine("       " + line);
            }
        }

        if (WritesTheFieldTests(spec, plan))
        {
            output.WriteLine();
            output.WriteLine($" +. {TestDatabasePath(spec)}: AppDbContext.{f}Schema, before AppDbContext.PlatformSchema,");
            output.WriteLine("     so the database fixture truncates the table the feature's integration test writes to");
        }
        else if (spec.HasApi)
        {
            output.WriteLine();
            output.WriteLine($"When integration tests exercise the feature, AppDbContext.{f}Schema also joins the schemas their database");
            output.WriteLine("fixture truncates between tests (Tests/Integration, TestDatabase).");
        }

        if (spec.Fields is { } fields)
        {
            ReportFields(output, spec, fields);
        }

        output.WriteLine();

        if (mode != ReportMode.Applied)
        {
            output.WriteLine(mode == ReportMode.DryRun
                ? "Then a run without --dry-run runs, in this order, and stops at the first that fails:"
                : "Then, once the edits are made:");

            foreach (var step in FeatureGeneration.Steps(root, spec))
            {
                output.WriteLine($"  {step.Display}");
            }

            output.WriteLine();
        }

        if (!record)
        {
            output.WriteLine("The aggregate raises no event. The first one it raises needs a consumer, or its name in");
            output.WriteLine("DomainEventTests._deliberatelyUnconsumed with the reason (docs/guides/adding-a-feature.md lists the closed lists).");
            output.WriteLine();
        }

        output.WriteLine("Not generated, because it is a decision rather than a shape: the rules the feature protects, any domain");

        if (spec.HasApi)
        {
            output.WriteLine("event of its own, authorisation beyond default-deny, and the integration tests against PostgreSQL,");
            output.WriteLine("a read of every response member on the wire among them.");
        }
        else
        {
            output.WriteLine("event of its own, authorisation beyond default-deny, and what each host shows of it.");
        }

        if (spec.Concurrency == "none")
        {
            output.WriteLine();
            output.WriteLine("--concurrency none: an update replaces what is stored whatever its state, so two concurrent writers");
            output.WriteLine(spec.HasApi
                ? "are last-write-wins, and the API publishes no ETag and reads no If-Match."
                : "are last-write-wins.");
        }
    }

    internal static void ReportFields(TextWriter output, Spec spec, IReadOnlyList<Field> fields)
    {
        output.WriteLine();
        output.WriteLine($"--fields: {string.Join(", ", fields.Select(field => field.Declaration))}.");
        output.WriteLine("Each is checked twice, by the validator and by the aggregate or the service, stored in a column of its");
        output.WriteLine("length or precision, and carried by the contracts and the generated tests.");

        if (spec.Has("list"))
        {
            var sortable = fields.Where(FieldCode.IsSortable).Select(field => field.Name).ToList();

            output.WriteLine(sortable.Count == 0
                ? "No field sorts the list: only required string(n), int, long, DateOnly and DateTimeOffset fields do, each with its index."
                : $"The list sorts by {string.Join(", ", sortable)} and createdAt, each with its index; a text, a decimal, a bool,");
            output.WriteLine(sortable.Count == 0
                ? "A text is long and unindexed, SQLite cannot order a decimal, and a nullable field orders apart on each provider."
                : "a Guid and a nullable field do not, being long, unordered on SQLite, or ordered apart on each provider.");
        }

        if (spec.Deletion == "anonymise" && fields.Where(field => FieldCode.Anonymised(field) is null).Select(field => field.Name).ToList() is { Count: > 0 } kept)
        {
            output.WriteLine($"--deletion anonymise rewrites the strings, the texts and the nullable fields, and keeps {string.Join(", ", kept)}:");
            output.WriteLine("rewrite whatever of them still says who the owner was.");
        }
    }

    internal static void ReportPage(TextWriter output, Spec spec, Emission? page, string? unwritten, ReportMode mode)
    {
        string path = FeaturePage.PagePath(spec.Features);
        bool applied = mode == ReportMode.Applied;

        output.WriteLine();

        if (page is null)
        {
            output.WriteLine($"Feature page: {unwritten}");
        }
        else
        {
            output.WriteLine(applied
                ? $"Feature page: {path}, from {FeaturePage._pageTemplate}. The manifest records it, so remove-feature deletes it, edited or not."
                : $"Feature page: {path}, from {FeaturePage._pageTemplate}, written with the files above.");
            output.WriteLine("Each heading says what its section holds; replace those lines with what this feature does.");
        }

        output.WriteLine(applied
            ? "docs-index lists it in the index of docs/features, by the title and the description of its frontmatter."
            : "Then docs-index lists it in the index of docs/features, by the title and the description of its frontmatter.");

        if (spec.HasApi)
        {
            output.WriteLine(applied
                ? "Its section, added to the route table under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads:"
                : "And its section of the route table under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads:");

            foreach (string line in FeatureEdits.RouteSection(spec))
            {
                output.WriteLine("  " + line);
            }
        }

        if (spec.HasApi && Flags(spec).Contains("notfound"))
        {
            output.WriteLine(applied
                ? "The row of its refusal, added under ### Resources in docs/reference/api.md, which the error-code tests read:"
                : "And the row of its refusal under ### Resources in docs/reference/api.md, which the error-code tests read:");
            output.WriteLine($"  {ErrorRow(spec)}");
        }
    }

    internal static string ErrorRow(Spec spec) => $"| `{CSharpNames.Lower(spec.Type)}.notFound` | 404 | Missing, or another caller's |";
}
