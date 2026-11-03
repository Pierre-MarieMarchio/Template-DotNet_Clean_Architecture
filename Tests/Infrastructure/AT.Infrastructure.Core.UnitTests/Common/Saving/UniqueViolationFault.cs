using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Saving;

internal static class UniqueViolationFault
{
    internal static DbUpdateException Create() =>
        new(
            "duplicate key",
            new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));
}
