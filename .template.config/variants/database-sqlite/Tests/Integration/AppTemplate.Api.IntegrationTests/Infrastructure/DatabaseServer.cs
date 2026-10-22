using Testcontainers.PostgreSql;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class DatabaseServer(string database) : IAsyncDisposable
{
    internal const string Image =
        "postgres:18.6-alpine3.23@sha256:885cf05d376c7cf27afef02073e6bdac3841252537f16e244fd1c1e6a7c99fb1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase(database)
        .WithUsername("apptemplate_tests")
        .WithPassword("apptemplate_tests")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task StartAsync(CancellationToken cancellationToken) => _container.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _container.DisposeAsync();

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
