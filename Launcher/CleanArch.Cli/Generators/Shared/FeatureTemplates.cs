namespace CleanArch.Cli.Generators.Shared;

internal static class FeatureTemplates
{
    private const string _extension = ".cs.txt";

    private static readonly string _folder = typeof(FeatureTemplates).Namespace + ".Templates.";

    private static readonly Lazy<Dictionary<string, string>> _templates = new(Load);

    internal static string Aggregate => Read(nameof(Aggregate));

    internal static string RepositoryPort => Read(nameof(RepositoryPort));

    internal static string ContentDto => Read(nameof(ContentDto));

    internal static string Dto => Read(nameof(Dto));

    internal static string SummaryDto => Read(nameof(SummaryDto));

    internal static string ListQuery => Read(nameof(ListQuery));

    internal static string Errors => Read(nameof(Errors));

    internal static string CollectionPolicy => Read(nameof(CollectionPolicy));

    internal static string InputPolicy => Read(nameof(InputPolicy));

    internal static string Filter => Read(nameof(Filter));

    internal static string QueriesPort => Read(nameof(QueriesPort));

    internal static string StorePort => Read(nameof(StorePort));

    internal static string ChangeStatus => Read(nameof(ChangeStatus));

    internal static string ServicePort => Read(nameof(ServicePort));

    internal static string Service => Read(nameof(Service));

    internal static string ContentValidator => Read(nameof(ContentValidator));

    internal static string DtoMapping => Read(nameof(DtoMapping));

    internal static string AccessServicePort => Read(nameof(AccessServicePort));

    internal static string AccessService => Read(nameof(AccessService));

    internal static string GetQueryValidator => Read(nameof(GetQueryValidator));

    internal static string GetUseCase => Read(nameof(GetUseCase));

    internal static string ListUseCase => Read(nameof(ListUseCase));

    internal static string CreateCommandValidator => Read(nameof(CreateCommandValidator));

    internal static string CreateUseCase => Read(nameof(CreateUseCase));

    internal static string UpdateCommandValidator => Read(nameof(UpdateCommandValidator));

    internal static string UpdateUseCase => Read(nameof(UpdateUseCase));

    internal static string DeleteCommandValidator => Read(nameof(DeleteCommandValidator));

    internal static string DeleteUseCase => Read(nameof(DeleteUseCase));

    internal static string RecordPlain => Read(nameof(RecordPlain));

    internal static string RecordAudited => Read(nameof(RecordAudited));

    internal static string Configuration => Read(nameof(Configuration));

    internal static string Store => Read(nameof(Store));

    internal static string MapperPort => Read(nameof(MapperPort));

    internal static string Mapper => Read(nameof(Mapper));

    internal static string TrackerPort => Read(nameof(TrackerPort));

    internal static string Tracker => Read(nameof(Tracker));

    internal static string Repository => Read(nameof(Repository));

    internal static string AccountDeletedConsumer => Read(nameof(AccountDeletedConsumer));

    internal static string DeletedAccountDataPolicy => Read(nameof(DeletedAccountDataPolicy));

    internal static string UserAccountDirectory => Read(nameof(UserAccountDirectory));

    internal static string SweepUseCase => Read(nameof(SweepUseCase));

    internal static string DeletedAccountSweepJobs => Read(nameof(DeletedAccountSweepJobs));

    internal static string DeletedAccountSweepOptions => Read(nameof(DeletedAccountSweepOptions));

    internal static string LocalUserAccountDirectory => Read(nameof(LocalUserAccountDirectory));

    internal static string ExternalUserAccountDirectory => Read(nameof(ExternalUserAccountDirectory));

    internal static string Queries => Read(nameof(Queries));

    internal static string SortMap => Read(nameof(SortMap));

    internal static string CreateRequest => Read(nameof(CreateRequest));

    internal static string UpdateRequest => Read(nameof(UpdateRequest));

    internal static string ListRequest => Read(nameof(ListRequest));

    internal static string Response => Read(nameof(Response));

    internal static string SummaryResponse => Read(nameof(SummaryResponse));

    internal static string ResponseMapping => Read(nameof(ResponseMapping));

    internal static string Controller => Read(nameof(Controller));

    internal static string ContentValidatorTests => Read(nameof(ContentValidatorTests));

    internal static string CommandValidatorTests => Read(nameof(CommandValidatorTests));

    internal static string ServiceTests => Read(nameof(ServiceTests));

    internal static string AggregateTests => Read(nameof(AggregateTests));

    internal static string ConfigurationTests => Read(nameof(ConfigurationTests));

    internal static string MapperFidelityTests => Read(nameof(MapperFidelityTests));

    internal static string FieldContentValidatorTests => Read(nameof(FieldContentValidatorTests));

    internal static string FieldCommandValidatorTests => Read(nameof(FieldCommandValidatorTests));

    internal static string FieldAggregateTests => Read(nameof(FieldAggregateTests));

    internal static string FieldMapperFidelityTests => Read(nameof(FieldMapperFidelityTests));

    internal static string FieldApiTests => Read(nameof(FieldApiTests));

    internal static string FieldSortMap => Read(nameof(FieldSortMap));

    private static string Read(string name) =>
        _templates.Value.TryGetValue(name, out string? template)
            ? template
            : throw new InvalidOperationException($"No template is called {name}: {_folder}{name}{_extension} is not embedded in the tool.");

    private static Dictionary<string, string> Load()
    {
        var assembly = typeof(FeatureTemplates).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(_folder, StringComparison.Ordinal) && resource.EndsWith(_extension, StringComparison.Ordinal))
            .ToDictionary(resource => resource[_folder.Length..^_extension.Length], resource => Text(assembly, resource), StringComparer.Ordinal);
    }

    private static string Text(System.Reflection.Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        string text = reader.ReadToEnd();

        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
