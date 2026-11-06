using System.Text.Json.Nodes;
using static CleanArch.Cli.Generators.Shared.CSharpNames;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.Shared;

internal static class FeatureCode
{
    internal const string Generator = "dotnet cleanarch add feature";

    internal const string ScriptGenerator = "Tools/NewFeature.cs";

    internal static string TestDatabasePath(FeatureSpec spec) => $"{IntegrationTests(spec)}/Infrastructure/TestDatabase.cs";

    internal static bool WritesTheFieldTests(FeatureSpec spec, List<Emission> plan) =>
        plan.Any(emission => emission.Path.StartsWith(IntegrationTests(spec) + "/", StringComparison.Ordinal));

    internal static string IntegrationTests(FeatureSpec spec) => ProjectOf(spec, "Tests/Integration", "Api.IntegrationTests");

    internal static string PersistenceProject(FeatureSpec spec) => ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence");

    internal static string? Preconditions(string root, FeatureSpec spec, TextWriter output, IExecutor executor)
    {
        string auth = ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Auth");
        string[] contexts =
        [
            PersistenceProject(spec),
            .. Directory.Exists(System.IO.Path.Combine(root, auth)) ? [auth] : Array.Empty<string>(),
        ];

        string[] missing = [.. contexts.Where(context => !HasInitialMigration(root, context))];

        if (missing.Length > 0)
        {
            return $"{string.Join(" and ", missing)} has no initial migration, so the migration of {spec.Features} would have "
                + "none to follow. Run `dotnet run Tools/Tasks.cs bootstrap`, or `dotnet run Tools/Tasks.cs migration-initial`, "
                + "and commit the migrations first (docs/guides/migrations.md).";
        }

        return MigrationCheck.Refusal(root, output, executor, $"The migration of {spec.Features}");
    }

    internal static bool HasInitialMigration(string root, string context)
    {
        string folder = System.IO.Path.Combine(root, context, "Migrations");

        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Any();
    }

    internal static bool? TablePrefixesOf(LocatedProject project, List<string> problems)
    {
        try
        {
            return TablePrefixes(project);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }
    }

    internal static bool TablePrefixes(LocatedProject project) =>
        project.File.Options.Database switch
        {
            "postgresql" or "sqlserver" => false,
            "sqlite" => true,
            _ => throw new InvalidOperationException(
                $"'{Path.Combine(project.Root, ProjectLocator.FileName)}' records no options.database the generator knows (postgresql, sqlserver or sqlite), "
                + "and the feature's table test depends on it: the provider decides whether a schema holds the table or prefixes its name."),
        };

    internal static IEnumerable<string> PresentationHosts(string root, string prefix)
    {
        string presentation = Full(root, Presentation);

        return Directory.Exists(presentation)
            ? Directory.GetDirectories(presentation)
                .Where(folder => File.Exists(System.IO.Path.Combine(folder, "Program.cs")))
                .Where(folder => !IsClientPresentation(folder))
                .Select(System.IO.Path.GetFileName)
                .OfType<string>()
                .Where(host => host != FolderOf(prefix, "Api"))
                .Order(StringComparer.Ordinal)
            : [];
    }

    internal static string[] ComposingHosts(string root, FeatureSpec spec) =>
        spec.HasApi
            ? [.. PresentationHosts(root, spec.Prefix).Prepend(FolderOf(spec.Prefix, "Api"))]
            : [.. PresentationHosts(root, spec.Prefix)];

    internal static string JobsHost(string root, string prefix)
    {
        string presentation = Full(root, Presentation);

        return (Directory.Exists(presentation)
            ? Directory.GetDirectories(presentation)
                .Where(folder => File.Exists(System.IO.Path.Combine(folder, "Features", "BackgroundJobs", "Jobs", "BackgroundJobsModule.cs")))
                .Select(System.IO.Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .FirstOrDefault()
            : null) ?? FolderOf(prefix, "Api");
    }

    internal static bool IsClientPresentation(string folder) =>
        Directory.EnumerateFiles(folder, "*.csproj")
            .Any(project => File.ReadAllText(project).Contains(".Presentation.Client.csproj", StringComparison.Ordinal));

    internal static string HostComposition(string root, string host, string adapter)
    {
        string folder = Full(root, $"{Presentation}/{host}");
        string module = adapter == "Auth" ? "AddAuthModule(" : "AddExternalIdentityPersistence(";

        string? found = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(file => File.ReadAllText(file).Contains(module, StringComparison.Ordinal));

        return System.IO.Path.GetRelativePath(root, found ?? System.IO.Path.Combine(folder, "Program.cs")).Replace('\\', '/');
    }

    internal static string ProjectOf(FeatureSpec spec, string layer, string project) =>
        $"{layer}/{FolderOf(spec.Prefix, project)}";

    internal static string ProjectInFolder(string prefix, string folder) => prefix + folder[Initials(prefix).Length..];

    internal static List<Emission> Plan(FeatureSpec spec, string root, out List<string> skippedTestProjects)
    {
        var plan = new PlanWriter(spec, root);

        if (spec.Reaction is { } adapter)
        {
            plan.Reaction(adapter);
        }

        plan.Domain();
        plan.Application();
        plan.UseCases();
        plan.Persistence();

        if (spec.HasApi)
        {
            plan.Api();
        }

        plan.UnitTests();
        plan.FieldTests();
        skippedTestProjects = plan.Skipped;

        return plan.Emissions;
    }

    internal static HashSet<string> Flags(FeatureSpec spec)
    {
        var flags = new HashSet<string>(spec.Operations, StringComparer.Ordinal)
        {
            spec.Access,
            IsRecord(spec) ? "rec" : "agg",
        };

        foreach (var (flag, _) in OptionFlags(spec).Concat(OperationFlags(spec)).Concat(FieldFlags(spec)).Where(candidate => candidate.Holds))
        {
            flags.Add(flag);
        }

        return flags;
    }

    internal static Dictionary<string, string> Tokens(FeatureSpec spec)
    {
        var flags = Flags(spec);
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        NameTokens(tokens, spec);
        OwnershipTokens(tokens, spec);
        ConcurrencyTokens(tokens, spec);
        ModelTokens(tokens, spec);
        DeletionTokens(tokens, spec);
        tokens["ServiceArgs"] = ServiceArguments(spec, flags);
        CallTokens(tokens, spec);
        DocumentationTokens(tokens, spec, flags);

        if (spec.Fields is { } fields)
        {
            FieldTokens(tokens, spec, fields);
        }

        return tokens;
    }

    internal static void FieldTokens(Dictionary<string, string> tokens, FeatureSpec spec, IReadOnlyList<Field> fields)
    {
        var writer = new FieldTokenWriter(tokens, spec, fields, Flags(spec));

        writer.Aggregate();
        writer.Contracts();
        writer.Persistence();
        writer.Sorting();
        writer.Tests();
    }

    internal static string SortCases(IEnumerable<Field> sortable, string source, string verb) =>
        FieldCode.Joined(sortable.Select(field => FieldCode.Joined(
        [
            $"            %A%CollectionPolicy.{field.Name}Field => term.Direction == SortDirection.Ascending",
            $"                ? {source}.{verb}(record => record.{field.Name})",
            $"                : {source}.{verb}Descending(record => record.{field.Name}),",
        ])));

    private static bool IsRecord(FeatureSpec spec) => spec.Model == "record";

    private static bool IsOwner(FeatureSpec spec) => spec.Access == "owner";

    private static bool HasEtag(FeatureSpec spec) => spec.Concurrency == "etag";

    private static bool Anonymises(FeatureSpec spec) => spec.Deletion == "anonymise";

    private static bool HasAny(FeatureSpec spec, params string[] operations) => operations.Any(spec.Has);

    private static (string Flag, bool Holds)[] OptionFlags(FeatureSpec spec) =>
    [
        ("etag", HasEtag(spec)),
        ("purge", spec.Deletion == "delete"),
        ("anon", Anonymises(spec)),
        ("prefixed", spec.TablePrefixes),
    ];

    private static (string Flag, bool Holds)[] OperationFlags(FeatureSpec spec)
    {
        bool record = IsRecord(spec);
        bool single = spec.Has("get") || (record && HasAny(spec, "create", "update"));

        return
        [
            ("wq", HasAny(spec, "create", "update", "delete")),
            ("change", HasAny(spec, "update", "delete")),
            ("content", HasAny(spec, "create", "update")),
            ("dto", HasAny(spec, "get", "create", "update")),
            ("single", single),
            ("rq", single || spec.Has("list") || IsOwner(spec)),
            ("stage", record && (HasAny(spec, "create", "update", "delete") || IsOwner(spec))),
            ("notfound", HasAny(spec, "get", "update", "delete") || (record && spec.Has("create"))),
            ("clock", record && HasAny(spec, "create", "update")),
        ];
    }

    private static (string Flag, bool Holds)[] FieldFlags(FeatureSpec spec)
    {
        var fields = spec.Fields;

        return
        [
            ("fields", fields is not null),
            ("anontext", Anonymises(spec) && (fields is null || fields.Any(field => field is { Kind: FieldKind.String, Nullable: false }))),
            ("trim", HasAny(spec, "create", "update") && (fields is null || fields.Any(field => field.Kind == FieldKind.String))),
            ("policy", fields is null || fields.Any(FieldCode.HasConstants)),
        ];
    }

    private static void NameTokens(Dictionary<string, string> tokens, FeatureSpec spec)
    {
        string lower = CSharpNames.Lower(spec.Type);

        tokens["P"] = spec.Prefix;
        tokens["LowerP"] = spec.Prefix.ToLowerInvariant();
        tokens["F"] = spec.Features;
        tokens["A"] = spec.Type;
        tokens["L"] = lower;
        tokens["R"] = Kebab(spec.Features);
        tokens["S"] = Snake(spec.Features);
        tokens["a"] = IsVowel(lower[0]) ? "an" : "a";
        tokens["An"] = IsVowel(lower[0]) ? "An" : "A";
    }

    private static void OwnershipTokens(Dictionary<string, string> tokens, FeatureSpec spec)
    {
        bool owner = IsOwner(spec);

        tokens["OwnerClause"] = owner ? " && record.OwnerId == ownerId.Value" : string.Empty;
        tokens["ListQuery"] = owner ? "GetForOwnerAsync" : "GetPageAsync";
        tokens["SvcOwner"] = owner ? "userId.Value, " : string.Empty;
        tokens["Implements"] = owner ? ", IOwnedAggregate" : string.Empty;
        tokens["Load"] = owner ? "LoadOwnedAsync" : "LoadAsync";
        tokens["Scope"] = owner ? "caller's own " : string.Empty;
        tokens["OwnerArg"] = owner ? "ownerId, " : string.Empty;
        tokens["OwnerIdArg"] = owner ? "_ownerId, " : string.Empty;
        tokens["AnyOwner"] = owner ? "Arg.Any<UserId>(), " : string.Empty;
    }

    private static void ConcurrencyTokens(Dictionary<string, string> tokens, FeatureSpec spec)
    {
        bool etag = HasEtag(spec);

        tokens["ReadDto"] = etag ? $"Versioned<{spec.Type}Dto>" : $"{spec.Type}Dto";
        tokens["ReadResponse"] = etag ? $"Versioned<{spec.Type}Response>" : $"{spec.Type}Response";
        tokens["Precondition"] = etag ? ", precondition" : string.Empty;
        tokens["NullPrecondition"] = etag ? ", null" : string.Empty;
        tokens["AnyPrecondition"] = etag ? "Arg.Any<VersionPrecondition?>(), " : string.Empty;
    }

    private static void ModelTokens(Dictionary<string, string> tokens, FeatureSpec spec)
    {
        bool record = IsRecord(spec);
        string features = spec.Features;

        tokens["Limits"] = Limits(spec);
        tokens["ListInput"] = record ? $"{spec.Type}ListQueryDto" : $"Get{features}Query";
        tokens["ListInputNamespace"] = record
            ? $"{spec.Prefix}.Application.Features.{features}.Model.Dtos"
            : $"{spec.Prefix}.Application.Features.{features}.UseCases.Queries.Get{features}";
        tokens["CtorParams"] = record ? $"I{spec.Type}Service service" : "ISender sender";
    }

    private static string Limits(FeatureSpec spec) => IsRecord(spec) ? $"{spec.Type}InputPolicy" : spec.Type;

    private static void DeletionTokens(Dictionary<string, string> tokens, FeatureSpec spec)
    {
        bool anonymises = Anonymises(spec);

        tokens["Verb"] = anonymises ? "Anonymise" : "Delete";
        tokens["Fate"] = anonymises ? "anonymised" : "deleted";
        tokens["Stage"] = anonymises ? "StageAnonymisationForOwnerAsync" : "StageRemovalForOwnerAsync";
    }

    private static string ServiceArguments(FeatureSpec spec, HashSet<string> flags) =>
        string.Join(
            ", ",
            [
                .. flags.Contains("rq") ? ["_queries"] : Array.Empty<string>(),
                .. flags.Contains("wq") ? ["_store", "_unitOfWork"] : Array.Empty<string>(),
                .. flags.Contains("clock") ? ["TheClock.Start()"] : Array.Empty<string>(),
                .. flags.Contains("content") ? [$"new {spec.Type}ContentDtoValidator()"] : Array.Empty<string>(),
                "currentUser",
            ]);

    private static void CallTokens(Dictionary<string, string> tokens, FeatureSpec spec)
    {
        string type = spec.Type;
        string id = $"{CSharpNames.Lower(type)}Id";
        string precondition = HasEtag(spec) ? ", precondition" : string.Empty;
        string content = spec.Fields is { } fields
            ? string.Join(", ", fields.Select(field => $"request.{field.Name}"))
            : "request.Name, request.Description";

        if (IsRecord(spec))
        {
            tokens["CallList"] = "service.ListAsync(query, cancellationToken)";
            tokens["CallGet"] = $"service.GetAsync({id}, cancellationToken)";
            tokens["CallCreate"] = $"service.CreateAsync(new {type}ContentDto({content}), cancellationToken)";
            tokens["CallUpdate"] = $"service.UpdateAsync({id}, new {type}ContentDto({content}){precondition}, cancellationToken)";
            tokens["CallDelete"] = $"service.DeleteAsync({id}{precondition}, cancellationToken)";
        }
        else
        {
            tokens["CallList"] = "sender.Send(query, cancellationToken)";
            tokens["CallGet"] = $"sender.Send(new Get{type}Query({id}), cancellationToken)";
            tokens["CallCreate"] = $"sender.Send(new Create{type}Command({content}), cancellationToken)";
            tokens["CallUpdate"] = $"sender.Send(new Update{type}Command({id}, {content}{precondition}), cancellationToken)";
            tokens["CallDelete"] = $"sender.Send(new Delete{type}Command({id}{precondition}), cancellationToken)";
        }
    }

    private static void DocumentationTokens(Dictionary<string, string> tokens, FeatureSpec spec, HashSet<string> flags)
    {
        var fields = spec.Fields;
        bool generic = fields is null;

        tokens["Staged"] = flags.Contains("trim") ? "Trimmed(content)" : "content";
        tokens["Replaced"] = generic ? "name and description" : "fields";
        tokens["Revised"] = generic ? "text" : "fields";
        tokens["ListTail"] = Omission(fields, ", names only", ", without their texts");
        tokens["SummaryDoc"] = Omission(fields, ", without its description", ", without its texts");
        tokens["SortDoc"] = string.Join(
            ", ",
            (fields?.Where(FieldCode.IsSortable).Select(field => CSharpNames.Lower(field.Name)) ?? ["name"])
                .Append("createdAt")
                .Select(field => $"<c>{field}</c>"));
        tokens["DateKeys"] = string.Join(
            ", ",
            (fields ?? [])
                .Where(field => FieldCode.IsKeyset(field) && field.Kind == FieldKind.DateTimeOffset)
                .Select(field => $"{spec.Type}CollectionPolicy.{field.Name}Field")
                .Prepend($"{spec.Type}CollectionPolicy.CreatedAtField"));
    }

    private static string Omission(IReadOnlyList<Field>? fields, string placeholders, string texts)
    {
        if (fields is null)
        {
            return placeholders;
        }

        return fields.Any(field => !FieldCode.InSummary(field)) ? texts : string.Empty;
    }

    internal static string KeysetMethod(Field field) =>
        field.Kind == FieldKind.String
            ? $$"""
                    private static IQueryable<%A%Record> Apply{{field.Name}}Keyset(
                        IQueryable<%A%Record> source,
                        SortDirection direction,
                        Cursor cursor) =>
                        direction == SortDirection.Ascending
                            ? source.Where(record =>
                                record.{{field.Name}}.CompareTo(cursor.Key) > 0
                                || (record.{{field.Name}} == cursor.Key && record.Id.CompareTo(cursor.Id) > 0))
                            : source.Where(record =>
                                record.{{field.Name}}.CompareTo(cursor.Key) < 0
                                || (record.{{field.Name}} == cursor.Key && record.Id.CompareTo(cursor.Id) < 0));
                """
            : $$"""
                    private static IQueryable<%A%Record> Apply{{field.Name}}Keyset(
                        IQueryable<%A%Record> source,
                        SortDirection direction,
                        Cursor cursor)
                    {
                        if (!DateTimeOffset.TryParse(
                            cursor.Key,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out DateTimeOffset parsed))
                        {
                            throw new InvalidOperationException(
                                "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
                        }

                        var key = parsed.ToUniversalTime();

                        return direction == SortDirection.Ascending
                            ? source.Where(record =>
                                record.{{field.Name}}.CompareTo(key) > 0
                                || (record.{{field.Name}} == key && record.Id.CompareTo(cursor.Id) > 0))
                            : source.Where(record =>
                                record.{{field.Name}}.CompareTo(key) < 0
                                || (record.{{field.Name}} == key && record.Id.CompareTo(cursor.Id) < 0));
                    }
                """;

    internal static string ManifestPath(FeatureSpec spec) => FeatureManifest.PathOf(spec.Features);

    internal static JsonArray FieldsOf(IReadOnlyList<Field> fields)
    {
        var written = new JsonArray();

        foreach (var field in fields)
        {
            var entry = new JsonObject
            {
                ["name"] = field.Name,
                ["declaration"] = field.Declaration,
                ["kind"] = field.Kind.ToString(),
                ["nullable"] = field.Nullable,
            };

            if (FieldCode.IsReference(field))
            {
                entry["maxLength"] = field.Length;
            }

            if (field.Kind == FieldKind.Decimal)
            {
                entry["precision"] = field.Precision;
                entry["scale"] = field.Scale;
            }

            foreach (var bound in field.Bounds)
            {
                entry[char.ToLowerInvariant(bound.Suffix[0]) + bound.Suffix[1..]] = bound.Literal;
            }

            entry["sortable"] = FieldCode.IsSortable(field);
            entry["inSummary"] = FieldCode.InSummary(field);
            written.Add((JsonNode)entry);
        }

        return written;
    }

    private sealed class PlanWriter(FeatureSpec spec, string root)
    {
        private const string _feature = "Features/%F%";

        private const string _queries = $"{_feature}/UseCases/Queries";

        private const string _commands = $"{_feature}/UseCases/Commands";

        private readonly HashSet<string> _flags = Flags(spec);

        private readonly Dictionary<string, string> _tokens = Tokens(spec);

        internal List<Emission> Emissions { get; } = [];

        internal List<string> Skipped { get; } = [];

        internal void Reaction(string adapter)
        {
            string core = ProjectOf(spec, "Src/Application", "Application.Core");
            string jobsHost = JobsHost(root, spec.Prefix);
            var jobs = new Dictionary<string, string>(StringComparer.Ordinal) { ["J"] = ProjectInFolder(spec.Prefix, jobsHost) };

            Add(core, "Common/Ports/IDeletedAccountDataPolicy.cs", FeatureTemplates.DeletedAccountDataPolicy);
            Add(core, "Common/Ports/IUserAccountDirectory.cs", FeatureTemplates.UserAccountDirectory);
            Add(core, "Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData.cs", FeatureTemplates.SweepUseCase);
            Add($"Src/Presentation/{jobsHost}", "Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs", FeatureTemplates.DeletedAccountSweepJobs, extra: jobs);
            Add($"Src/Presentation/{jobsHost}", "Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs", FeatureTemplates.DeletedAccountSweepOptions, extra: jobs);

            var (relative, template) = adapter == "Auth"
                ? ("Features/Auth/Composition/AccountUserAccountDirectory.cs", FeatureTemplates.LocalUserAccountDirectory)
                : ("Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs", FeatureTemplates.ExternalUserAccountDirectory);

            foreach (string host in ComposingHosts(root, spec))
            {
                var named = new Dictionary<string, string>(StringComparer.Ordinal) { ["H"] = ProjectInFolder(spec.Prefix, host) };

                Add($"Src/Presentation/{host}", relative, template, extra: named);
            }
        }

        internal void Domain()
        {
            string domain = ProjectOf(spec, "Src/Domain", "Domain");

            Add(domain, $"{_feature}/Model/Entities/%A%.cs", FeatureTemplates.Aggregate, "agg");
            Add(domain, $"{_feature}/Repositories/I%A%Repository.cs", FeatureTemplates.RepositoryPort, "agg&wq");
        }

        internal void Application()
        {
            string application = ProjectOf(spec, "Src/Application", "Application");

            Add(application, $"{_feature}/Model/Dtos/%A%ContentDto.cs", FeatureTemplates.ContentDto, "rec&content");
            Add(application, $"{_feature}/Model/Dtos/%A%Dto.cs", FeatureTemplates.Dto, "dto");
            Add(application, $"{_feature}/Model/Dtos/%A%SummaryDto.cs", FeatureTemplates.SummaryDto, "list");
            Add(application, $"{_feature}/Model/Dtos/%A%ListQueryDto.cs", FeatureTemplates.ListQuery, "rec&list");
            Add(application, $"{_feature}/Model/Errors/%A%Errors.cs", FeatureTemplates.Errors, "notfound");
            Add(application, $"{_feature}/Model/Policies/%A%CollectionPolicy.cs", FeatureTemplates.CollectionPolicy, "list");
            Add(application, $"{_feature}/Model/Policies/%A%InputPolicy.cs", FeatureTemplates.InputPolicy, "rec&policy");
            Add(application, $"{_feature}/Ports/%A%Queries/I%A%Queries.cs", FeatureTemplates.QueriesPort, "rq");
            Add(application, $"{_feature}/Ports/%A%Queries/%A%Filter.cs", FeatureTemplates.Filter, "list");
            Add(application, $"{_feature}/Ports/%A%Store/I%A%Store.cs", FeatureTemplates.StorePort, "rec&stage");
            Add(application, $"{_feature}/Ports/%A%Store/%A%ChangeStatus.cs", FeatureTemplates.ChangeStatus, "rec&change");
            Add(application, $"{_feature}/Services/I%A%Service.cs", FeatureTemplates.ServicePort, "rec");
            Add(application, $"{_feature}/Services/%A%Service.cs", FeatureTemplates.Service, "rec");
            Add(application, $"{_feature}/Model/Dtos/%A%ContentDtoValidator.cs", FeatureTemplates.ContentValidator, "rec&content");
            Add(application, $"{_feature}/Model/Mapping/%A%DtoMapping.cs", FeatureTemplates.DtoMapping, "agg&content");
            Add(application, $"{_feature}/Services/I%A%Service.cs", FeatureTemplates.AccessServicePort, "agg&change");
            Add(application, $"{_feature}/Services/%A%Service.cs", FeatureTemplates.AccessService, "agg&change");
        }

        internal void UseCases()
        {
            string application = ProjectOf(spec, "Src/Application", "Application");

            Add(application, $"{_queries}/Get%A%/Get%A%.cs", FeatureTemplates.GetUseCase, "agg&get");
            Add(application, $"{_queries}/Get%A%/Get%A%QueryValidator.cs", FeatureTemplates.GetQueryValidator, "agg&get");
            Add(application, $"{_queries}/Get%F%/Get%F%.cs", FeatureTemplates.ListUseCase, "agg&list");
            Add(application, $"{_commands}/Create%A%/Create%A%.cs", FeatureTemplates.CreateUseCase, "agg&create");
            Add(application, $"{_commands}/Create%A%/Create%A%CommandValidator.cs", FeatureTemplates.CreateCommandValidator, "agg&create");
            Add(application, $"{_commands}/Update%A%/Update%A%.cs", FeatureTemplates.UpdateUseCase, "agg&update");
            Add(application, $"{_commands}/Update%A%/Update%A%CommandValidator.cs", FeatureTemplates.UpdateCommandValidator, "agg&update");
            Add(application, $"{_commands}/Delete%A%/Delete%A%.cs", FeatureTemplates.DeleteUseCase, "agg&delete");
            Add(application, $"{_commands}/Delete%A%/Delete%A%CommandValidator.cs", FeatureTemplates.DeleteCommandValidator, "agg&delete");
            Add(application, $"{_feature}/Services/Consumers/%Verb%%A%OnAccountDeletedConsumer.cs", FeatureTemplates.AccountDeletedConsumer, "owner");
        }

        internal void Persistence()
        {
            string persistence = PersistenceProject(spec);

            Add(persistence, $"{_feature}/Rows/Models/%A%Record.cs", FeatureTemplates.RecordPlain, "rec");
            Add(persistence, $"{_feature}/Rows/Models/%A%Record.cs", FeatureTemplates.RecordAudited, "agg");
            Add(persistence, $"{_feature}/Rows/Configurations/%A%RecordConfiguration.cs", FeatureTemplates.Configuration);
            Add(persistence, $"{_feature}/Access/Stores/%A%Store.cs", FeatureTemplates.Store, "rec&stage");
            Add(persistence, $"{_feature}/Rows/Mapping/I%A%Mapper.cs", FeatureTemplates.MapperPort, "agg");
            Add(persistence, $"{_feature}/Rows/Mapping/%A%Mapper.cs", FeatureTemplates.Mapper, "agg");
            Add(persistence, $"{_feature}/Rows/Tracking/I%A%Tracker.cs", FeatureTemplates.TrackerPort, "agg");
            Add(persistence, $"{_feature}/Rows/Tracking/%A%Tracker.cs", FeatureTemplates.Tracker, "agg");
            Add(persistence, $"{_feature}/Access/Repositories/%A%Repository.cs", FeatureTemplates.Repository, "agg&wq");
            Add(persistence, $"{_feature}/Access/Queries/%A%Queries.cs", FeatureTemplates.Queries, "rq");
            Add(persistence, $"{_feature}/Access/Queries/%A%SortMap.cs", FeatureTemplates.SortMap, "list&!fields");
            Add(persistence, $"{_feature}/Access/Queries/%A%SortMap.cs", FeatureTemplates.FieldSortMap, "list&fields");
        }

        internal void Api()
        {
            string api = ProjectOf(spec, "Src/Presentation", "Api");

            Add(api, $"{_feature}/Contracts/Requests/Create%A%Request.cs", FeatureTemplates.CreateRequest, "create");
            Add(api, $"{_feature}/Contracts/Requests/Update%A%Request.cs", FeatureTemplates.UpdateRequest, "update");
            Add(api, $"{_feature}/Contracts/Requests/Get%F%Request.cs", FeatureTemplates.ListRequest, "list");
            Add(api, $"{_feature}/Contracts/Responses/%A%Response.cs", FeatureTemplates.Response, "dto");
            Add(api, $"{_feature}/Contracts/Responses/%A%SummaryResponse.cs", FeatureTemplates.SummaryResponse, "list");
            Add(api, $"{_feature}/Controllers/%F%Controller.cs", FeatureTemplates.Controller);
            Add(api, $"{_feature}/Contracts/Mapping/%A%ResponseMapping.cs", FeatureTemplates.ResponseMapping, "dto|list");
        }

        internal void UnitTests()
        {
            string applicationTests = ProjectOf(spec, "Tests/Application", "Application.UnitTests");
            string domainTests = ProjectOf(spec, "Tests/Domain", "Domain.UnitTests");
            string persistenceTests = ProjectOf(spec, "Tests/Infrastructure", "Infrastructure.Persistence.UnitTests");

            AddTest(applicationTests, $"{_feature}/Model/Dtos/%A%ContentDtoValidatorTests.cs", FeatureTemplates.ContentValidatorTests, "rec&content&!fields");
            AddTest(applicationTests, $"{_feature}/Model/Dtos/%A%ContentDtoValidatorTests.cs", FeatureTemplates.FieldContentValidatorTests, "rec&content&fields");
            AddTest(applicationTests, $"{_feature}/Services/%A%ServiceTests.cs", FeatureTemplates.ServiceTests, "rec");
            CommandValidatorTests(applicationTests);
            AddTest(domainTests, $"{_feature}/Model/Entities/%A%Tests.cs", FeatureTemplates.AggregateTests, "agg&!fields");
            AddTest(domainTests, $"{_feature}/Model/Entities/%A%Tests.cs", FeatureTemplates.FieldAggregateTests, "agg&fields");
            AddTest(persistenceTests, $"{_feature}/Rows/Configurations/%A%RecordConfigurationTests.cs", FeatureTemplates.ConfigurationTests);
            AddTest(persistenceTests, $"{_feature}/%A%MapperFidelityTests.cs", FeatureTemplates.MapperFidelityTests, "agg&!fields");
            AddTest(persistenceTests, $"{_feature}/%A%MapperFidelityTests.cs", FeatureTemplates.FieldMapperFidelityTests, "agg&fields");
        }

        internal void FieldTests()
        {
            if (spec.HasApi && spec.Fields is not null && spec.Has("create"))
            {
                AddTest(IntegrationTests(spec), $"{_feature}/%A%FieldTests.cs", FeatureTemplates.FieldApiTests);
            }
        }

        private void CommandValidatorTests(string applicationTests)
        {
            if (spec.Fields is { } fields)
            {
                FieldCommandValidatorTests(applicationTests, fields, "Create");
                FieldCommandValidatorTests(applicationTests, fields, "Update");
            }

            AddTest(
                applicationTests,
                $"{_commands}/Create%A%/Create%A%CommandValidatorTests.cs",
                FeatureTemplates.CommandValidatorTests,
                "agg&create&!fields",
                new() { ["Op"] = "Create", ["CommandNew"] = "new Create%A%Command(name!, description!)" });
            AddTest(
                applicationTests,
                $"{_commands}/Update%A%/Update%A%CommandValidatorTests.cs",
                FeatureTemplates.CommandValidatorTests,
                "agg&update&!fields",
                new() { ["Op"] = "Update", ["CommandNew"] = "new Update%A%Command(Guid.CreateVersion7(), name!, description!)" });
        }

        private void FieldCommandValidatorTests(string applicationTests, IReadOnlyList<Field> fields, string operation)
        {
            var samples = fields.Select(field => FieldCode.Sample(field, 1));

            AddTest(
                applicationTests,
                $"{_commands}/{operation}%A%/{operation}%A%CommandValidatorTests.cs",
                FeatureTemplates.FieldCommandValidatorTests,
                $"agg&{operation.ToLowerInvariant()}",
                new()
                {
                    ["Op"] = operation,
                    ["FValidatorTests"] = FieldCode.ValidatorTests(fields, $"{operation}{spec.Type}Command", "AValidCommand", spec.Type),
                    ["FValidCommand"] = FieldCode.Construction(operation == "Create" ? samples : samples.Prepend("Guid.CreateVersion7()")),
                });
        }

        private void Add(string project, string relative, string template, string when = "", Dictionary<string, string>? extra = null)
        {
            if (when.Length > 0 && !Template.Evaluate(when, _flags))
            {
                return;
            }

            var tokens = extra is null ? _tokens : _tokens.Concat(extra).ToDictionary(pair => pair.Key, pair => pair.Value);

            Emissions.Add(new Emission(
                $"{project}/{Template.Substitute(relative, tokens)}",
                Template.Render(template, _flags, tokens)));
        }

        private void AddTest(string project, string relative, string template, string when = "", Dictionary<string, string>? extra = null)
        {
            if (Directory.Exists(System.IO.Path.Combine(root, project)))
            {
                Add(project, relative, template, when, extra);
            }
            else if (!Skipped.Contains(project, StringComparer.Ordinal))
            {
                Skipped.Add(project);
            }
        }
    }

    private sealed class FieldTokenWriter
    {
        private readonly Dictionary<string, string> _tokens;

        private readonly FeatureSpec _spec;

        private readonly IReadOnlyList<Field> _fields;

        private readonly HashSet<string> _flags;

        private readonly string _limits;

        private readonly List<Field> _summary;

        private readonly List<Field> _sortable;

        private readonly List<Field> _anonymised;

        internal FieldTokenWriter(Dictionary<string, string> tokens, FeatureSpec spec, IReadOnlyList<Field> fields, HashSet<string> flags)
        {
            _tokens = tokens;
            _spec = spec;
            _fields = fields;
            _flags = flags;
            _limits = Limits(spec);
            _summary = [.. fields.Where(FieldCode.InSummary)];
            _sortable = [.. fields.Where(FieldCode.IsSortable)];
            _anonymised = [.. fields.Where(field => FieldCode.Anonymised(field) is not null)];
        }

        internal void Aggregate()
        {
            _tokens["FConstants"] = FieldCode.Constants(_fields, ceilings: !IsRecord(_spec) && _flags.Contains("content"));
            _tokens["FPolicyConstants"] = FieldCode.Constants(_fields, ceilings: false);
            _tokens["FCtorParams"] = FieldCode.List(_fields.Select(Declared), "        ", ") : base(id)");
            _tokens["FAssign"] = FieldCode.Joined(_fields.Select(field => $"        {field.Name} = {FieldCode.Parameter(field)};"));
            _tokens["FProperties"] = FieldCode.Joined(_fields.Select(field => $"    public {FieldCode.Type(field)} {field.Name} {Setter(field)}"), "\n\n");
            _tokens["FCreateParams"] = FieldCode.List(_fields.Select(Declared), "        ", ") =>");
            _tokens["FCreateArgs"] = FieldCode.List(_fields.Select(Required), "            ", ");");
            _tokens["FRehydrateParams"] = FieldCode.List(_fields.Select(Declared), "        ", ")");
            _tokens["FRehydrateArgs"] = FieldCode.List(_fields.Select(FieldCode.Parameter), "            ", ");");
            _tokens["FUpdateParams"] = string.Join(", ", _fields.Select(Declared));
            _tokens["FUpdateBody"] = FieldCode.Joined(
            [
                .. _fields.Where(FieldCode.HasRequire).Select(field => $"        {FieldCode.Type(field)} valid{field.Name} = {Required(field)};"),
                string.Empty,
                .. _fields.Select(field => $"        {field.Name} = {Validated(field)};"),
            ]);
            _tokens["FAnonymise"] = FieldCode.Joined(_anonymised.Select(field => $"        {field.Name} = {FieldCode.Anonymised(field)};"));
            _tokens["FRequire"] = FieldCode.Joined(_fields.Where(FieldCode.HasRequire).Select(FieldCode.Require), "\n\n");
        }

        internal void Contracts()
        {
            _tokens["FRulesCommand"] = FieldCode.Rules(_fields, "command", _spec.Type);
            _tokens["FRulesContent"] = FieldCode.Rules(_fields, "content", _limits);
            _tokens["FParamList"] = FieldCode.List(_fields.Select(field => $"{FieldCode.Type(field)} {field.Name}"), "    ");
            _tokens["FDtoParams"] = FieldCode.Joined(_fields.Select(field => $"    {FieldCode.Type(field)} {field.Name},"));
            _tokens["FSummaryParams"] = FieldCode.Joined(_summary.Select(field => $"    {FieldCode.Type(field)} {field.Name},"));
            _tokens["FCommandArgs"] = string.Join(", ", _fields.Select(field => $"command.{field.Name}"));
            _tokens["FDetailArgs"] = FieldCode.Joined(_fields.Select(field => $"            %L%.{field.Name},"));
            _tokens["FSummaryArgs"] = string.Concat(_summary.Select(field => $"record.{field.Name}, "));
            _tokens["FResponseArgs"] = string.Concat(_fields.Select(field => $"%L%.{field.Name}, "));
            _tokens["FSummaryResponseArgs"] = string.Concat(_summary.Select(field => $"summary.{field.Name}, "));
            _tokens["FRequestDocs"] = FieldCode.Joined(_fields.Select(FieldCode.WireDocumentation));
            _tokens["FRequestParams"] = FieldCode.List(_fields.Select(FieldCode.WireParameter), "    ");
            _tokens["FTrimmed"] = string.Join(
                ", ",
                _fields.Where(field => field.Kind == FieldKind.String)
                    .Select(field => $"{field.Name} = content.{field.Name}{(field.Nullable ? "?" : string.Empty)}.Trim()"));
        }

        internal void Persistence()
        {
            _tokens["FRecordProperties"] = FieldCode.Joined(_fields.Select(FieldCode.RecordProperty), "\n\n");
            _tokens["FConfiguration"] = FieldCode.Joined(_fields.Select(field => FieldCode.Configuration(field, _limits)), "\n\n");
            _tokens["FIndexes"] = FieldCode.Joined(_sortable.Select(field => FieldCode.SortIndex(_spec.Features, field, IsOwner(_spec))), "\n\n");
            _tokens["FStoreAssign"] = FieldCode.Joined(_fields.Select(field => $"            {field.Name} = {FieldCode.Store(field, "content")},"));
            _tokens["FStoreUpdate"] = FieldCode.Joined(_fields.Select(field => $"            record.{field.Name} = {FieldCode.Store(field, "content")};"));
            _tokens["FStoreAnonymise"] = FieldCode.Joined(_anonymised.Select(field => $"            record.{field.Name} = {FieldCode.Anonymised(field)};"));
            _tokens["FMapperRehydrate"] = FieldCode.List(_fields.Select(field => $"record.{field.Name}"), "            ", ");");
            _tokens["FMapperNew"] = FieldCode.Joined(_fields.Select(field => $"            {field.Name} = aggregate.{field.Name},"));
            _tokens["FMapperWrite"] = FieldCode.Joined(_fields.Select(field => $"        record.{field.Name} = aggregate.{field.Name};"));
            _tokens["FRecordArgs"] = string.Join(", ", _fields.Select(field => $"record.{field.Name}"));
            _tokens["FRecordArgLines"] = FieldCode.Joined(_fields.Select(field => $"                record.{field.Name},"));
        }

        internal void Sorting()
        {
            var keyset = _sortable.Where(FieldCode.IsKeyset).ToList();

            _tokens["FPolicyFields"] = FieldCode.Joined(_sortable.Select(field => $"    public const string {field.Name}Field = \"{CSharpNames.Lower(field.Name)}\";"), "\n\n");
            _tokens["FSortables"] = FieldCode.Joined(_sortable.Select(field =>
                $"        SortableField.{(FieldCode.IsKeyset(field) ? "Keyset" : "OffsetOnly")}({field.Name}Field),"));
            _tokens["FKeysetCases"] = FieldCode.Joined(keyset.Select(field =>
                $"            %A%CollectionPolicy.{field.Name}Field => Apply{field.Name}Keyset(source, term.Direction, cursor),"));
            _tokens["FKeyCases"] = FieldCode.Joined(keyset.Select(KeyCase));
            _tokens["FOrderCases"] = SortCases(_sortable, "source", "OrderBy");
            _tokens["FThenCases"] = SortCases(_sortable, "source", "ThenBy");
            _tokens["FKeysetMethods"] = FieldCode.Joined(keyset.Select(KeysetMethod), "\n\n");
        }

        internal void Tests()
        {
            _tokens["FValidContent"] = FieldCode.Construction(_fields.Select(field => FieldCode.Sample(field, 1)));
            _tokens["FServiceInvalid"] = ServiceRefusal();
            _tokens["FContentValidatorTests"] = FieldCode.ValidatorTests(_fields, $"{_spec.Type}ContentDto", "AValidContentDto", _limits);
            _tokens["FAggregateTests"] = FieldCode.AggregateTests(_fields, _flags.Contains("create"), _flags.Contains("update"), "%OwnerIdArg%");
            _tokens["FConfigurationTests"] = FieldCode.ConfigurationTests(_fields, _limits);
            _tokens["FFidelityArgs"] = FieldCode.List(_fields.Select(field => FieldCode.Sample(field, 1)), "            ", ");");
            _tokens["FFidelityAsserts"] = FieldCode.Joined(_fields.Select(field => FieldCode.Assertion("roundTripped", field, FieldCode.Sample(field, 1))));
            _tokens["FRequestSample"] = FieldCode.Construction(_fields.Select(field => FieldCode.Sample(field, 1)));
            _tokens["FApiInstants"] = ApiInstants();
            _tokens["FResponseAsserts"] = FieldCode.Joined(_fields.Select(field => FieldCode.Assertion("created", field, FieldCode.Sample(field, 1))));
            _tokens["FApiRefusal"] = ApiRefusal();
        }

        private static string Declared(Field field) => $"{FieldCode.Type(field)} {FieldCode.Parameter(field)}";

        private static string Required(Field field) =>
            FieldCode.HasRequire(field) ? $"Require{field.Name}({FieldCode.Parameter(field)})" : FieldCode.Parameter(field);

        private static string Validated(Field field) => FieldCode.HasRequire(field) ? "valid" + field.Name : FieldCode.Parameter(field);

        private static string KeyCase(Field field) =>
            field.Kind == FieldKind.String
                ? $"            %A%CollectionPolicy.{field.Name}Field => summary.{field.Name},"
                : $"            %A%CollectionPolicy.{field.Name}Field => summary.{field.Name}.ToString(\"O\", CultureInfo.InvariantCulture),";

        private string Setter(Field field) =>
            _flags.Contains("update") || (_flags.Contains("anon") && FieldCode.Anonymised(field) is not null) ? "{ get; private set; }" : "{ get; }";

        private string ServiceRefusal()
        {
            if (_fields.FirstOrDefault(field => FieldCode.Invalid(field, _limits).Count > 0) is not { } refused)
            {
                return string.Empty;
            }

            return FieldCode.Joined(
            [
                "    [Fact]",
                "    public async Task CreateAsync_RejectsInvalidContent_AndStagesNothing()",
                "    {",
                $"        var result = await Service().CreateAsync(AValidContent with {{ {refused.Name} = {FieldCode.Invalid(refused, _limits)[0].Value} }}, TestToken);",
                string.Empty,
                "        result.Error!.Type.ShouldBe(ErrorType.Validation);",
                "        _store.ReceivedCalls().ShouldBeEmpty();",
                "        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());",
                "    }",
            ]);
        }

        private string ApiInstants()
        {
            var instants = _fields.Where(field => field.Kind == FieldKind.DateTimeOffset).ToList();

            if (instants.Count == 0)
            {
                return string.Empty;
            }

            return FieldCode.Joined(
            [
                "    [Fact]",
                "    public async Task EveryDateTimeOffset_SentWithAnOffset_IsKeptAsTheSameInstant_InUtc()",
                "    {",
                "        var (client, _, _) = await SignInAsync();",
                "        var sent = new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2));",
                string.Empty,
                $"        using var response = await client.PostAsJsonAsync(_route, AValidRequest() with {{ {string.Join(", ", instants.Select(field => $"{field.Name} = sent"))} }}, TestToken);",
                string.Empty,
                "        response.StatusCode.ShouldBe(HttpStatusCode.Created);",
                string.Empty,
                "        var created = await ApiJson.ReadAsync<%A%Response>(response, TestToken);",
                string.Empty,
                .. instants.SelectMany(field => new[]
                {
                    $"        created.{field.Name}.ShouldBe(sent);",
                    $"        created.{field.Name}{(field.Nullable ? "!.Value" : string.Empty)}.Offset.ShouldBe(TimeSpan.Zero);",
                }),
                "    }",
            ]);
        }

        private string ApiRefusal()
        {
            var refusal = _fields
                .SelectMany(field => FieldCode.Invalid(field, null).Where(invalid => invalid.Case != "Missing").Select(invalid => (Field: field, invalid.Case, invalid.Value)))
                .FirstOrDefault();

            if (refusal.Field is null)
            {
                return string.Empty;
            }

            return FieldCode.Joined(
            [
                "    [Fact]",
                $"    public async Task {refusal.Field.Name}{refusal.Case}_Answers400_WithTheValidationProblem()",
                "    {",
                "        var (client, _, _) = await SignInAsync();",
                string.Empty,
                $"        using var response = await client.PostAsJsonAsync(_route, AValidRequest() with {{ {refusal.Field.Name} = {refusal.Value} }}, TestToken);",
                string.Empty,
                "        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);",
                "        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe(\"request.validationFailed\");",
                "    }",
            ]);
        }
    }
}
