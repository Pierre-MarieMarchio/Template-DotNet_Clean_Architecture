using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;

internal static class DatabaseFaults
{
    internal const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none";

    internal static DbUpdateException UniqueViolation() =>
        new(
            "duplicate key",
            new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
}
