using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;

internal static class DatabaseFaults
{
    internal const string UnreachableConnectionString =
        "Data Source=unreachable/none.db;Mode=ReadOnly";

    private const int _constraintViolation = 19;

    private const int _uniqueViolation = 2067;

    internal static DbUpdateException UniqueViolation() =>
        new(
            "duplicate key",
            new SqliteException("UNIQUE constraint failed", _constraintViolation, _uniqueViolation));
}
