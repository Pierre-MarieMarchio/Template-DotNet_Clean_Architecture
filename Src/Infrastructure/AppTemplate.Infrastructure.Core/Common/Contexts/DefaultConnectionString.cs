using Microsoft.Extensions.Configuration;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

public static class DefaultConnectionString
{
    public const string Name = "Default";

    public const string EnvironmentVariableName = "ConnectionStrings__Default";

    private const string _localDesignTimeFallback =
        "Host=localhost;Port=5432;Database=appdb;Username=postgres;Password=postgres";

    public static string Require(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? connectionString = configuration.GetConnectionString(Name);

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"The '{Name}' connection string is not configured. Set ConnectionStrings:{Name}.")
            : connectionString;
    }

    public static string ResolveForDesignTime()
    {
        string? connectionString = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        return string.IsNullOrWhiteSpace(connectionString) ? _localDesignTimeFallback : connectionString;
    }
}
