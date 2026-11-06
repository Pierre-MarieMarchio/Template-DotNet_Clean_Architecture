using System.Text.Json;
using System.Text.Json.Nodes;
using static CleanArch.Cli.Generators.Shared.CSharpNames;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.Shared;

internal static class FeatureCode
{
    internal const string Generator = "dotnet cleanarch add feature";

    internal const string ScriptGenerator = "Tools/NewFeature.cs";

    internal sealed record Spec(
        string Prefix,
        string Features,
        string Type,
        string Access,
        string Deletion,
        string[] Operations,
        string Model,
        string Concurrency,
        string? Reaction,
        bool HasApi,
        IReadOnlyList<Field>? Fields = null,
        bool TablePrefixes = false)
    {
        internal bool Has(string operation) => Operations.Contains(operation, StringComparer.Ordinal);
    }

    internal static string TestDatabasePath(Spec spec) => $"{IntegrationTests(spec)}/Infrastructure/TestDatabase.cs";

    internal static bool WritesTheFieldTests(Spec spec, List<Emission> plan) =>
        plan.Any(emission => emission.Path.StartsWith(IntegrationTests(spec) + "/", StringComparison.Ordinal));

    internal static string IntegrationTests(Spec spec) => ProjectOf(spec, "Tests/Integration", "Api.IntegrationTests");

    internal static string PersistenceProject(Spec spec) => ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence");

    internal static string? Preconditions(string root, Spec spec, TextWriter output, Func<TextEdits.Command, int> run)
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

        return MigrationCheck.Refusal(root, output, run, $"The migration of {spec.Features}");
    }

    internal static bool HasInitialMigration(string root, string context)
    {
        string folder = System.IO.Path.Combine(root, context, "Migrations");

        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Any();
    }

    internal static bool TablePrefixes(string root)
    {
        string projectFile = System.IO.Path.Combine(root, "cleanarch.json");

        using var document = JsonDocument.Parse(File.ReadAllText(projectFile));

        string? database = document.RootElement.TryGetProperty("options", out JsonElement options)
            && options.TryGetProperty("database", out JsonElement recorded)
                ? recorded.GetString()
                : null;

        return database switch
        {
            "postgresql" or "sqlserver" => false,
            "sqlite" => true,
            _ => throw new InvalidOperationException(
                $"'{projectFile}' records no options.database the generator knows (postgresql, sqlserver or sqlite), "
                + "and the feature's table test depends on it: the provider decides whether a schema holds the table or prefixes its name."),
        };
    }

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

    internal static string[] ComposingHosts(string root, Spec spec) =>
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

    internal static string ProjectOf(Spec spec, string layer, string project) =>
        $"{layer}/{FolderOf(spec.Prefix, project)}";

    internal static string ProjectInFolder(string prefix, string folder) => prefix + folder[Initials(prefix).Length..];

    internal static List<Emission> Plan(Spec spec, string root, out List<string> skippedTestProjects)
    {
        var flags = Flags(spec);
        var tokens = Tokens(spec);
        var emissions = new List<Emission>();
        var skipped = new List<string>();

        void Add(string project, string relative, string template, string when = "", Dictionary<string, string>? extra = null)
        {
            if (when.Length > 0 && !Template.Evaluate(when, flags))
            {
                return;
            }

            var all = extra is null ? tokens : tokens.Concat(extra).ToDictionary(pair => pair.Key, pair => pair.Value);

            emissions.Add(new Emission(
                $"{project}/{Template.Substitute(relative, all)}",
                Template.Render(template, flags, all)));
        }

        void AddTest(string project, string relative, string template, string when = "", Dictionary<string, string>? extra = null)
        {
            if (!Directory.Exists(System.IO.Path.Combine(root, project)))
            {
                if (!skipped.Contains(project, StringComparer.Ordinal))
                {
                    skipped.Add(project);
                }

                return;
            }

            Add(project, relative, template, when, extra);
        }

        string domain = ProjectOf(spec, "Src/Domain", "Domain");
        string application = ProjectOf(spec, "Src/Application", "Application");
        string applicationCore = ProjectOf(spec, "Src/Application", "Application.Core");
        string persistence = ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence");
        string api = ProjectOf(spec, "Src/Presentation", "Api");

        string applicationTests = ProjectOf(spec, "Tests/Application", "Application.UnitTests");
        string domainTests = ProjectOf(spec, "Tests/Domain", "Domain.UnitTests");
        string persistenceTests = ProjectOf(spec, "Tests/Infrastructure", "Infrastructure.Persistence.UnitTests");

        string feature = "Features/%F%";

        if (spec.Reaction is { } adapter)
        {
            Add(applicationCore, "Common/Ports/IDeletedAccountDataPolicy.cs", FeatureTemplates.DeletedAccountDataPolicy);
            Add(applicationCore, "Common/Ports/IUserAccountDirectory.cs", FeatureTemplates.UserAccountDirectory);
            Add(applicationCore, "Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData.cs", FeatureTemplates.SweepUseCase);
            string jobsHost = JobsHost(root, spec.Prefix);
            var jobs = new Dictionary<string, string>(StringComparer.Ordinal) { ["J"] = ProjectInFolder(spec.Prefix, jobsHost) };

            Add($"Src/Presentation/{jobsHost}", "Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs", FeatureTemplates.DeletedAccountSweepJobs, extra: jobs);
            Add($"Src/Presentation/{jobsHost}", "Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs", FeatureTemplates.DeletedAccountSweepOptions, extra: jobs);

            foreach (string host in ComposingHosts(root, spec))
            {
                var named = new Dictionary<string, string>(StringComparer.Ordinal) { ["H"] = ProjectInFolder(spec.Prefix, host) };

                if (adapter == "Auth")
                {
                    Add($"Src/Presentation/{host}", "Features/Auth/Composition/AccountUserAccountDirectory.cs", FeatureTemplates.LocalUserAccountDirectory, extra: named);
                }
                else
                {
                    Add($"Src/Presentation/{host}", "Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs", FeatureTemplates.ExternalUserAccountDirectory, extra: named);
                }
            }
        }

        Add(domain, $"{feature}/Model/Entities/%A%.cs", FeatureTemplates.Aggregate, "agg");
        Add(domain, $"{feature}/Repositories/I%A%Repository.cs", FeatureTemplates.RepositoryPort, "agg&wq");

        Add(application, $"{feature}/Model/Dtos/%A%ContentDto.cs", FeatureTemplates.ContentDto, "rec&content");
        Add(application, $"{feature}/Model/Dtos/%A%Dto.cs", FeatureTemplates.Dto, "dto");
        Add(application, $"{feature}/Model/Dtos/%A%SummaryDto.cs", FeatureTemplates.SummaryDto, "list");
        Add(application, $"{feature}/Model/Dtos/%A%ListQueryDto.cs", FeatureTemplates.ListQuery, "rec&list");
        Add(application, $"{feature}/Model/Errors/%A%Errors.cs", FeatureTemplates.Errors, "notfound");
        Add(application, $"{feature}/Model/Policies/%A%CollectionPolicy.cs", FeatureTemplates.CollectionPolicy, "list");
        Add(application, $"{feature}/Model/Policies/%A%InputPolicy.cs", FeatureTemplates.InputPolicy, "rec&policy");
        Add(application, $"{feature}/Ports/%A%Queries/I%A%Queries.cs", FeatureTemplates.QueriesPort, "rq");
        Add(application, $"{feature}/Ports/%A%Queries/%A%Filter.cs", FeatureTemplates.Filter, "list");
        Add(application, $"{feature}/Ports/%A%Store/I%A%Store.cs", FeatureTemplates.StorePort, "rec&stage");
        Add(application, $"{feature}/Ports/%A%Store/%A%ChangeStatus.cs", FeatureTemplates.ChangeStatus, "rec&change");

        Add(application, $"{feature}/Services/I%A%Service.cs", FeatureTemplates.ServicePort, "rec");
        Add(application, $"{feature}/Services/%A%Service.cs", FeatureTemplates.Service, "rec");
        Add(application, $"{feature}/Model/Dtos/%A%ContentDtoValidator.cs", FeatureTemplates.ContentValidator, "rec&content");

        Add(application, $"{feature}/Model/Mapping/%A%DtoMapping.cs", FeatureTemplates.DtoMapping, "agg&content");
        Add(application, $"{feature}/Services/I%A%Service.cs", FeatureTemplates.AccessServicePort, "agg&change");
        Add(application, $"{feature}/Services/%A%Service.cs", FeatureTemplates.AccessService, "agg&change");
        string queries = $"{feature}/UseCases/Queries";
        string commands = $"{feature}/UseCases/Commands";

        Add(application, $"{queries}/Get%A%/Get%A%.cs", FeatureTemplates.GetUseCase, "agg&get");
        Add(application, $"{queries}/Get%A%/Get%A%QueryValidator.cs", FeatureTemplates.GetQueryValidator, "agg&get");
        Add(application, $"{queries}/Get%F%/Get%F%.cs", FeatureTemplates.ListUseCase, "agg&list");
        Add(application, $"{commands}/Create%A%/Create%A%.cs", FeatureTemplates.CreateUseCase, "agg&create");
        Add(application, $"{commands}/Create%A%/Create%A%CommandValidator.cs", FeatureTemplates.CreateCommandValidator, "agg&create");
        Add(application, $"{commands}/Update%A%/Update%A%.cs", FeatureTemplates.UpdateUseCase, "agg&update");
        Add(application, $"{commands}/Update%A%/Update%A%CommandValidator.cs", FeatureTemplates.UpdateCommandValidator, "agg&update");
        Add(application, $"{commands}/Delete%A%/Delete%A%.cs", FeatureTemplates.DeleteUseCase, "agg&delete");
        Add(application, $"{commands}/Delete%A%/Delete%A%CommandValidator.cs", FeatureTemplates.DeleteCommandValidator, "agg&delete");

        Add(application, $"{feature}/Services/Consumers/%Verb%%A%OnAccountDeletedConsumer.cs", FeatureTemplates.AccountDeletedConsumer, "owner");

        Add(persistence, $"{feature}/Rows/Models/%A%Record.cs", FeatureTemplates.RecordPlain, "rec");
        Add(persistence, $"{feature}/Rows/Models/%A%Record.cs", FeatureTemplates.RecordAudited, "agg");
        Add(persistence, $"{feature}/Rows/Configurations/%A%RecordConfiguration.cs", FeatureTemplates.Configuration);
        Add(persistence, $"{feature}/Access/Stores/%A%Store.cs", FeatureTemplates.Store, "rec&stage");
        Add(persistence, $"{feature}/Rows/Mapping/I%A%Mapper.cs", FeatureTemplates.MapperPort, "agg");
        Add(persistence, $"{feature}/Rows/Mapping/%A%Mapper.cs", FeatureTemplates.Mapper, "agg");
        Add(persistence, $"{feature}/Rows/Tracking/I%A%Tracker.cs", FeatureTemplates.TrackerPort, "agg");
        Add(persistence, $"{feature}/Rows/Tracking/%A%Tracker.cs", FeatureTemplates.Tracker, "agg");
        Add(persistence, $"{feature}/Access/Repositories/%A%Repository.cs", FeatureTemplates.Repository, "agg&wq");
        Add(persistence, $"{feature}/Access/Queries/%A%Queries.cs", FeatureTemplates.Queries, "rq");
        Add(persistence, $"{feature}/Access/Queries/%A%SortMap.cs", FeatureTemplates.SortMap, "list&!fields");
        Add(persistence, $"{feature}/Access/Queries/%A%SortMap.cs", FeatureTemplates.FieldSortMap, "list&fields");

        if (spec.HasApi)
        {
            Add(api, $"{feature}/Contracts/Requests/Create%A%Request.cs", FeatureTemplates.CreateRequest, "create");
            Add(api, $"{feature}/Contracts/Requests/Update%A%Request.cs", FeatureTemplates.UpdateRequest, "update");
            Add(api, $"{feature}/Contracts/Requests/Get%F%Request.cs", FeatureTemplates.ListRequest, "list");
            Add(api, $"{feature}/Contracts/Responses/%A%Response.cs", FeatureTemplates.Response, "dto");
            Add(api, $"{feature}/Contracts/Responses/%A%SummaryResponse.cs", FeatureTemplates.SummaryResponse, "list");
            Add(api, $"{feature}/Controllers/%F%Controller.cs", FeatureTemplates.Controller);
            Add(api, $"{feature}/Contracts/Mapping/%A%ResponseMapping.cs", FeatureTemplates.ResponseMapping, "dto|list");
        }

        AddTest(applicationTests, $"{feature}/Model/Dtos/%A%ContentDtoValidatorTests.cs", FeatureTemplates.ContentValidatorTests, "rec&content&!fields");
        AddTest(applicationTests, $"{feature}/Model/Dtos/%A%ContentDtoValidatorTests.cs", FeatureTemplates.FieldContentValidatorTests, "rec&content&fields");
        AddTest(applicationTests, $"{feature}/Services/%A%ServiceTests.cs", FeatureTemplates.ServiceTests, "rec");
        foreach (string operation in spec.Fields is null ? [] : new[] { "Create", "Update" })
        {
            var fields = spec.Fields!;
            string message = $"{operation}{spec.Type}Command";

            AddTest(
                applicationTests,
                $"{feature}/UseCases/Commands/{operation}%A%/{operation}%A%CommandValidatorTests.cs",
                FeatureTemplates.FieldCommandValidatorTests,
                $"agg&{operation.ToLowerInvariant()}",
                new()
                {
                    ["Op"] = operation,
                    ["FValidatorTests"] = FieldCode.ValidatorTests(fields, message, "AValidCommand", spec.Type),
                    ["FValidCommand"] = FieldCode.Construction(
                        operation == "Create" ? fields.Select(field => FieldCode.Sample(field, 1)) : fields.Select(field => FieldCode.Sample(field, 1)).Prepend("Guid.CreateVersion7()")),
                });
        }

        AddTest(
            applicationTests,
            $"{feature}/UseCases/Commands/Create%A%/Create%A%CommandValidatorTests.cs",
            FeatureTemplates.CommandValidatorTests,
            "agg&create&!fields",
            new() { ["Op"] = "Create", ["CommandNew"] = "new Create%A%Command(name!, description!)" });
        AddTest(
            applicationTests,
            $"{feature}/UseCases/Commands/Update%A%/Update%A%CommandValidatorTests.cs",
            FeatureTemplates.CommandValidatorTests,
            "agg&update&!fields",
            new() { ["Op"] = "Update", ["CommandNew"] = "new Update%A%Command(Guid.CreateVersion7(), name!, description!)" });
        AddTest(domainTests, $"{feature}/Model/Entities/%A%Tests.cs", FeatureTemplates.AggregateTests, "agg&!fields");
        AddTest(domainTests, $"{feature}/Model/Entities/%A%Tests.cs", FeatureTemplates.FieldAggregateTests, "agg&fields");
        AddTest(persistenceTests, $"{feature}/Rows/Configurations/%A%RecordConfigurationTests.cs", FeatureTemplates.ConfigurationTests);
        AddTest(persistenceTests, $"{feature}/%A%MapperFidelityTests.cs", FeatureTemplates.MapperFidelityTests, "agg&!fields");
        AddTest(persistenceTests, $"{feature}/%A%MapperFidelityTests.cs", FeatureTemplates.FieldMapperFidelityTests, "agg&fields");

        if (spec.HasApi && spec.Fields is not null && spec.Has("create"))
        {
            AddTest(IntegrationTests(spec), $"{feature}/%A%FieldTests.cs", FeatureTemplates.FieldApiTests);
        }

        skippedTestProjects = skipped;

        return emissions;
    }

    internal static HashSet<string> Flags(Spec spec)
    {
        bool record = spec.Model == "record";
        var flags = new HashSet<string>(spec.Operations, StringComparer.Ordinal)
        {
            spec.Access,
            record ? "rec" : "agg",
        };

        void When(bool condition, string flag)
        {
            if (condition)
            {
                flags.Add(flag);
            }
        }

        bool get = spec.Has("get");
        bool list = spec.Has("list");
        bool create = spec.Has("create");
        bool update = spec.Has("update");
        bool delete = spec.Has("delete");
        bool owner = spec.Access == "owner";

        When(spec.Concurrency == "etag", "etag");
        When(spec.Deletion == "delete", "purge");
        When(spec.Deletion == "anonymise", "anon");

        When(create || update || delete, "wq");
        When(update || delete, "change");
        When(create || update, "content");

        When(get || create || update, "dto");

        When(get || (record && (create || update)), "single");
        When(flags.Contains("single") || list || owner, "rq");

        When(record && (create || update || delete || owner), "stage");

        When(get || update || delete || (record && create), "notfound");
        When(record && (create || update), "clock");

        var fields = spec.Fields;

        When(fields is not null, "fields");
        When(spec.Deletion == "anonymise" && (fields is null || fields.Any(field => field is { Kind: FieldKind.String, Nullable: false })), "anontext");
        When(flags.Contains("content") && (fields is null || fields.Any(field => field.Kind == FieldKind.String)), "trim");
        When(fields is null || fields.Any(FieldCode.HasConstants), "policy");
        When(spec.TablePrefixes, "prefixed");

        return flags;
    }

    internal static Dictionary<string, string> Tokens(Spec spec)
    {
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        bool etag = spec.Concurrency == "etag";

        string a = spec.Type;
        string f = spec.Features;
        string l = CSharpNames.Lower(a);

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P"] = spec.Prefix,
            ["LowerP"] = spec.Prefix.ToLowerInvariant(),
            ["F"] = f,
            ["A"] = a,
            ["L"] = l,
            ["R"] = Kebab(f),
            ["S"] = Snake(f),
            ["ReadDto"] = etag ? $"Versioned<{a}Dto>" : $"{a}Dto",
            ["ReadResponse"] = etag ? $"Versioned<{a}Response>" : $"{a}Response",
            ["OwnerClause"] = owner ? " && record.OwnerId == ownerId.Value" : string.Empty,
            ["Limits"] = record ? $"{a}InputPolicy" : a,
            ["ListQuery"] = owner ? "GetForOwnerAsync" : "GetPageAsync",
            ["ListInput"] = record ? $"{a}ListQueryDto" : $"Get{f}Query",
            ["ListInputNamespace"] = record
                ? $"{spec.Prefix}.Application.Features.{f}.Model.Dtos"
                : $"{spec.Prefix}.Application.Features.{f}.UseCases.Queries.Get{f}",
            ["SvcOwner"] = owner ? "userId.Value, " : string.Empty,
            ["Implements"] = owner ? ", IOwnedAggregate" : string.Empty,
            ["Load"] = owner ? "LoadOwnedAsync" : "LoadAsync",
            ["Precondition"] = etag ? ", precondition" : string.Empty,
            ["Scope"] = owner ? "caller's own " : string.Empty,
            ["a"] = IsVowel(l[0]) ? "an" : "a",
            ["An"] = IsVowel(l[0]) ? "An" : "A",
            ["OwnerArg"] = owner ? "ownerId, " : string.Empty,
            ["OwnerIdArg"] = owner ? "_ownerId, " : string.Empty,
            ["NullPrecondition"] = etag ? ", null" : string.Empty,
            ["AnyOwner"] = owner ? "Arg.Any<UserId>(), " : string.Empty,
            ["AnyPrecondition"] = etag ? "Arg.Any<VersionPrecondition?>(), " : string.Empty,
            ["Verb"] = spec.Deletion == "anonymise" ? "Anonymise" : "Delete",
            ["Fate"] = spec.Deletion == "anonymise" ? "anonymised" : "deleted",
            ["Stage"] = spec.Deletion == "anonymise" ? "StageAnonymisationForOwnerAsync" : "StageRemovalForOwnerAsync",
        };

        string[] serviceArguments =
        [
            .. Flags(spec).Contains("rq") ? ["_queries"] : Array.Empty<string>(),
            .. Flags(spec).Contains("wq") ? ["_store", "_unitOfWork"] : Array.Empty<string>(),
            .. Flags(spec).Contains("clock") ? ["TheClock.Start()"] : Array.Empty<string>(),
            .. Flags(spec).Contains("content") ? [$"new {a}ContentDtoValidator()"] : Array.Empty<string>(),
            "currentUser",
        ];

        tokens["ServiceArgs"] = string.Join(", ", serviceArguments);

        tokens["CtorParams"] = record ? $"I{a}Service service" : "ISender sender";

        string pre = etag ? ", precondition" : string.Empty;
        string content = spec.Fields is { } requested
            ? string.Join(", ", requested.Select(field => $"request.{field.Name}"))
            : "request.Name, request.Description";

        tokens["CallList"] = record
            ? "service.ListAsync(query, cancellationToken)"
            : "sender.Send(query, cancellationToken)";
        tokens["CallGet"] = record
            ? $"service.GetAsync({l}Id, cancellationToken)"
            : $"sender.Send(new Get{a}Query({l}Id), cancellationToken)";
        tokens["CallCreate"] = record
            ? $"service.CreateAsync(new {a}ContentDto({content}), cancellationToken)"
            : $"sender.Send(new Create{a}Command({content}), cancellationToken)";
        tokens["CallUpdate"] = record
            ? $"service.UpdateAsync({l}Id, new {a}ContentDto({content}){pre}, cancellationToken)"
            : $"sender.Send(new Update{a}Command({l}Id, {content}{pre}), cancellationToken)";
        tokens["CallDelete"] = record
            ? $"service.DeleteAsync({l}Id{pre}, cancellationToken)"
            : $"sender.Send(new Delete{a}Command({l}Id{pre}), cancellationToken)";

        tokens["Staged"] = Flags(spec).Contains("trim") ? "Trimmed(content)" : "content";
        tokens["Replaced"] = spec.Fields is null ? "name and description" : "fields";
        tokens["Revised"] = spec.Fields is null ? "text" : "fields";
        tokens["ListTail"] = spec.Fields is null ? ", names only"
            : spec.Fields.Any(field => !FieldCode.InSummary(field)) ? ", without their texts" : string.Empty;
        tokens["SummaryDoc"] = spec.Fields is null ? ", without its description"
            : spec.Fields.Any(field => !FieldCode.InSummary(field)) ? ", without its texts" : string.Empty;
        tokens["SortDoc"] = string.Join(
            ", ",
            (spec.Fields?.Where(FieldCode.IsSortable).Select(field => CSharpNames.Lower(field.Name)) ?? ["name"])
                .Append("createdAt")
                .Select(field => $"<c>{field}</c>"));
        tokens["DateKeys"] = string.Join(
            ", ",
            (spec.Fields ?? [])
                .Where(field => FieldCode.IsKeyset(field) && field.Kind == FieldKind.DateTimeOffset)
                .Select(field => $"{a}CollectionPolicy.{field.Name}Field")
                .Prepend($"{a}CollectionPolicy.CreatedAtField"));

        if (spec.Fields is { } fields)
        {
            FieldTokens(tokens, spec, fields);
        }

        return tokens;
    }

    internal static void FieldTokens(Dictionary<string, string> tokens, Spec spec, IReadOnlyList<Field> fields)
    {
        var flags = Flags(spec);
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        string a = spec.Type;
        string limits = record ? $"{a}InputPolicy" : a;
        var summary = fields.Where(FieldCode.InSummary).ToList();
        var sortable = fields.Where(FieldCode.IsSortable).ToList();
        var keyset = sortable.Where(FieldCode.IsKeyset).ToList();
        var anonymised = fields.Where(field => FieldCode.Anonymised(field) is not null).ToList();

        string Declared(Field field) => $"{FieldCode.Type(field)} {FieldCode.Parameter(field)}";

        string Setter(Field field) =>
            flags.Contains("update") || (flags.Contains("anon") && FieldCode.Anonymised(field) is not null) ? "{ get; private set; }" : "{ get; }";

        string Required(Field field) =>
            FieldCode.HasRequire(field) ? $"Require{field.Name}({FieldCode.Parameter(field)})" : FieldCode.Parameter(field);

        tokens["FConstants"] = FieldCode.Constants(fields, ceilings: !record && flags.Contains("content"));
        tokens["FPolicyConstants"] = FieldCode.Constants(fields, ceilings: false);
        tokens["FCtorParams"] = FieldCode.List(fields.Select(Declared), "        ", ") : base(id)");
        tokens["FAssign"] = FieldCode.Joined(fields.Select(field => $"        {field.Name} = {FieldCode.Parameter(field)};"));
        tokens["FProperties"] = FieldCode.Joined(fields.Select(field => $"    public {FieldCode.Type(field)} {field.Name} {Setter(field)}"), "\n\n");
        tokens["FCreateParams"] = FieldCode.List(fields.Select(Declared), "        ", ") =>");
        tokens["FCreateArgs"] = FieldCode.List(fields.Select(Required), "            ", ");");
        tokens["FRehydrateParams"] = FieldCode.List(fields.Select(Declared), "        ", ")");
        tokens["FRehydrateArgs"] = FieldCode.List(fields.Select(FieldCode.Parameter), "            ", ");");
        tokens["FUpdateParams"] = string.Join(", ", fields.Select(Declared));
        tokens["FUpdateBody"] = FieldCode.Joined(
        [
            .. fields.Where(FieldCode.HasRequire).Select(field => $"        {FieldCode.Type(field)} valid{field.Name} = {Required(field)};"),
            string.Empty,
            .. fields.Select(field => $"        {field.Name} = {(FieldCode.HasRequire(field) ? "valid" + field.Name : FieldCode.Parameter(field))};"),
        ]);
        tokens["FAnonymise"] = FieldCode.Joined(anonymised.Select(field => $"        {field.Name} = {FieldCode.Anonymised(field)};"));
        tokens["FRequire"] = FieldCode.Joined(fields.Where(FieldCode.HasRequire).Select(FieldCode.Require), "\n\n");
        tokens["FRulesCommand"] = FieldCode.Rules(fields, "command", a);
        tokens["FRulesContent"] = FieldCode.Rules(fields, "content", limits);
        tokens["FParamList"] = FieldCode.List(fields.Select(field => $"{FieldCode.Type(field)} {field.Name}"), "    ");
        tokens["FDtoParams"] = FieldCode.Joined(fields.Select(field => $"    {FieldCode.Type(field)} {field.Name},"));
        tokens["FSummaryParams"] = FieldCode.Joined(summary.Select(field => $"    {FieldCode.Type(field)} {field.Name},"));
        tokens["FCommandArgs"] = string.Join(", ", fields.Select(field => $"command.{field.Name}"));
        tokens["FDetailArgs"] = FieldCode.Joined(fields.Select(field => $"            %L%.{field.Name},"));
        tokens["FRecordProperties"] = FieldCode.Joined(fields.Select(FieldCode.RecordProperty), "\n\n");
        tokens["FConfiguration"] = FieldCode.Joined(fields.Select(field => FieldCode.Configuration(field, limits)), "\n\n");
        tokens["FIndexes"] = FieldCode.Joined(sortable.Select(field => FieldCode.SortIndex(spec.Features, field, owner)), "\n\n");
        tokens["FStoreAssign"] = FieldCode.Joined(fields.Select(field => $"            {field.Name} = {FieldCode.Store(field, "content")},"));
        tokens["FStoreUpdate"] = FieldCode.Joined(fields.Select(field => $"            record.{field.Name} = {FieldCode.Store(field, "content")};"));
        tokens["FStoreAnonymise"] = FieldCode.Joined(anonymised.Select(field => $"            record.{field.Name} = {FieldCode.Anonymised(field)};"));
        tokens["FMapperRehydrate"] = FieldCode.List(fields.Select(field => $"record.{field.Name}"), "            ", ");");
        tokens["FMapperNew"] = FieldCode.Joined(fields.Select(field => $"            {field.Name} = aggregate.{field.Name},"));
        tokens["FMapperWrite"] = FieldCode.Joined(fields.Select(field => $"        record.{field.Name} = aggregate.{field.Name};"));
        tokens["FRecordArgs"] = string.Join(", ", fields.Select(field => $"record.{field.Name}"));
        tokens["FRecordArgLines"] = FieldCode.Joined(fields.Select(field => $"                record.{field.Name},"));
        tokens["FSummaryArgs"] = string.Concat(summary.Select(field => $"record.{field.Name}, "));
        tokens["FResponseArgs"] = string.Concat(fields.Select(field => $"%L%.{field.Name}, "));
        tokens["FSummaryResponseArgs"] = string.Concat(summary.Select(field => $"summary.{field.Name}, "));
        tokens["FRequestDocs"] = FieldCode.Joined(fields.Select(FieldCode.WireDocumentation));
        tokens["FRequestParams"] = FieldCode.List(fields.Select(FieldCode.WireParameter), "    ");
        tokens["FTrimmed"] = string.Join(
            ", ",
            fields.Where(field => field.Kind == FieldKind.String)
                .Select(field => $"{field.Name} = content.{field.Name}{(field.Nullable ? "?" : string.Empty)}.Trim()"));
        tokens["FPolicyFields"] = FieldCode.Joined(sortable.Select(field => $"    public const string {field.Name}Field = \"{CSharpNames.Lower(field.Name)}\";"), "\n\n");
        tokens["FSortables"] = FieldCode.Joined(sortable.Select(field =>
            $"        SortableField.{(FieldCode.IsKeyset(field) ? "Keyset" : "OffsetOnly")}({field.Name}Field),"));
        tokens["FKeysetCases"] = FieldCode.Joined(keyset.Select(field =>
            $"            %A%CollectionPolicy.{field.Name}Field => Apply{field.Name}Keyset(source, term.Direction, cursor),"));
        tokens["FKeyCases"] = FieldCode.Joined(keyset.Select(field => field.Kind == FieldKind.String
            ? $"            %A%CollectionPolicy.{field.Name}Field => summary.{field.Name},"
            : $"            %A%CollectionPolicy.{field.Name}Field => summary.{field.Name}.ToString(\"O\", CultureInfo.InvariantCulture),"));
        tokens["FOrderCases"] = SortCases(sortable, "source", "OrderBy");
        tokens["FThenCases"] = SortCases(sortable, "source", "ThenBy");
        tokens["FKeysetMethods"] = FieldCode.Joined(keyset.Select(KeysetMethod), "\n\n");
        tokens["FValidContent"] = FieldCode.Construction(fields.Select(field => FieldCode.Sample(field, 1)));
        tokens["FServiceInvalid"] = fields.FirstOrDefault(field => FieldCode.Invalid(field, limits).Count > 0) is { } refused
            ? FieldCode.Joined(
            [
                "    [Fact]",
                "    public async Task CreateAsync_RejectsInvalidContent_AndStagesNothing()",
                "    {",
                $"        var result = await Service().CreateAsync(AValidContent with {{ {refused.Name} = {FieldCode.Invalid(refused, limits)[0].Value} }}, TestToken);",
                string.Empty,
                "        result.Error!.Type.ShouldBe(ErrorType.Validation);",
                "        _store.ReceivedCalls().ShouldBeEmpty();",
                "        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());",
                "    }",
            ])
            : string.Empty;
        tokens["FContentValidatorTests"] = FieldCode.ValidatorTests(fields, $"{a}ContentDto", "AValidContentDto", limits);
        tokens["FAggregateTests"] = FieldCode.AggregateTests(fields, flags.Contains("create"), flags.Contains("update"), "%OwnerIdArg%");
        tokens["FConfigurationTests"] = FieldCode.ConfigurationTests(fields, limits);
        tokens["FFidelityArgs"] = FieldCode.List(fields.Select(field => FieldCode.Sample(field, 1)), "            ", ");");
        tokens["FFidelityAsserts"] = FieldCode.Joined(fields.Select(field => FieldCode.Assertion("roundTripped", field, FieldCode.Sample(field, 1))));
        tokens["FRequestSample"] = FieldCode.Construction(fields.Select(field => FieldCode.Sample(field, 1)));
        tokens["FApiInstants"] = fields.Any(field => field.Kind == FieldKind.DateTimeOffset)
            ? FieldCode.Joined(
            [
                "    [Fact]",
                "    public async Task EveryDateTimeOffset_SentWithAnOffset_IsKeptAsTheSameInstant_InUtc()",
                "    {",
                "        var (client, _, _) = await SignInAsync();",
                "        var sent = new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2));",
                string.Empty,
                $"        using var response = await client.PostAsJsonAsync(_route, AValidRequest() with {{ {string.Join(", ", fields.Where(field => field.Kind == FieldKind.DateTimeOffset).Select(field => $"{field.Name} = sent"))} }}, TestToken);",
                string.Empty,
                "        response.StatusCode.ShouldBe(HttpStatusCode.Created);",
                string.Empty,
                "        var created = await ApiJson.ReadAsync<%A%Response>(response, TestToken);",
                string.Empty,
                .. fields.Where(field => field.Kind == FieldKind.DateTimeOffset).SelectMany(field => new[]
                {
                    $"        created.{field.Name}.ShouldBe(sent);",
                    $"        created.{field.Name}{(field.Nullable ? "!.Value" : string.Empty)}.Offset.ShouldBe(TimeSpan.Zero);",
                }),
                "    }",
            ])
            : string.Empty;
        tokens["FResponseAsserts"] = FieldCode.Joined(fields.Select(field => FieldCode.Assertion("created", field, FieldCode.Sample(field, 1))));
        tokens["FApiRefusal"] = fields
            .SelectMany(field => FieldCode.Invalid(field, null).Where(invalid => invalid.Case != "Missing").Select(invalid => (Field: field, invalid.Case, invalid.Value)))
            .Select(refusal => FieldCode.Joined(
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
            ]))
            .FirstOrDefault() ?? string.Empty;
    }

    internal static string SortCases(IEnumerable<Field> sortable, string source, string verb) =>
        FieldCode.Joined(sortable.Select(field => FieldCode.Joined(
        [
            $"            %A%CollectionPolicy.{field.Name}Field => term.Direction == SortDirection.Ascending",
            $"                ? {source}.{verb}(record => record.{field.Name})",
            $"                : {source}.{verb}Descending(record => record.{field.Name}),",
        ])));

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

    internal static string ManifestPath(Spec spec) => FeatureManifest.PathOf(spec.Features);

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
}
