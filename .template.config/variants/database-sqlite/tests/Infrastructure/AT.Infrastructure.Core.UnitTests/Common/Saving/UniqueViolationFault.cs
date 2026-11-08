using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Saving;

internal static class UniqueViolationFault
{
    private const int _constraintViolation = 19;

    private const int _uniqueViolation = 2067;

    internal static DbUpdateException Create() =>
        new(
            "duplicate key",
            new SqliteException("UNIQUE constraint failed", _constraintViolation, _uniqueViolation));
}
