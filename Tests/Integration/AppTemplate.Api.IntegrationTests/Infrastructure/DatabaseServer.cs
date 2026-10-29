using Testcontainers.PostgreSql;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class DatabaseServer(string database) : IAsyncDisposable
{
    internal const string Image =
        "postgres:18.6-alpine3.23@sha256:1a0c23d53c1ad77468c8653fa6eed4f4105cfbef9e6f027bc1dfeb16949a8c29";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase(database)
        .WithUsername("apptemplate_tests")
        .WithPassword("apptemplate_tests")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task StartAsync(CancellationToken cancellationToken) => _container.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    internal static string Table(string schema, string table) => $"{schema}.\"{table}\"";

    internal static object Value(Guid value) => value;

    internal static object Value(DateTimeOffset value) => value;

    internal static string Literal(Guid value) => $"'{value}'";

    internal static string Literal(DateTimeOffset value) => $"'{value:O}'";

    internal static string SchemasSql =>
        "SELECT DISTINCT table_schema FROM information_schema.tables WHERE table_type = 'BASE TABLE'";

    internal static string HistorySchemasSql(string historyTable) =>
        $"SELECT table_schema FROM information_schema.tables WHERE table_name = '{historyTable}' ORDER BY table_schema";

    internal static string TablesSql(IEnumerable<string> schemas, IEnumerable<string> keptTables) =>
        $"""
        SELECT '"' || table_schema || '"."' || table_name || '"'
        FROM information_schema.tables
        WHERE table_schema IN ({string.Join(", ", schemas.Select(schema => $"'{schema}'"))})
          AND table_type = 'BASE TABLE'
          AND table_name NOT IN ({string.Join(", ", keptTables.Select(table => $"'{table}'"))})
        ORDER BY table_schema, table_name
        """;

    internal static string ResetSql(IEnumerable<string> tables) =>
        $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE";
}
