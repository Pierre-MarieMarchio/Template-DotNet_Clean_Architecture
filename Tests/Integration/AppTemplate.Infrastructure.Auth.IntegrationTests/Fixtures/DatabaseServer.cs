using Npgsql;
using Testcontainers.PostgreSql;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

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

    internal static Exception TransientFault() =>
        new NpgsqlException("Injected: the connection was lost.", new TimeoutException());
}
