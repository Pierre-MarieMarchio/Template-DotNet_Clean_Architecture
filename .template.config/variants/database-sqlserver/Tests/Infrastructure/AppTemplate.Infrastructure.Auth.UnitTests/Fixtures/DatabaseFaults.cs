using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;

internal static class DatabaseFaults
{
    internal const string UnreachableConnectionString =
        "Server=127.0.0.1,1;Database=unreachable;User Id=none;Password=none;TrustServerCertificate=True;Connect Timeout=1;ConnectRetryCount=0";

    private const int _duplicateKeyConstraint = 2627;

    internal static DbUpdateException UniqueViolation() =>
        new("duplicate key", ASqlException(_duplicateKeyConstraint));

    private static SqlException ASqlException(int number)
    {
        const BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

        var error = typeof(SqlError).GetConstructors(nonPublic)
            .Single(constructor => constructor.GetParameters().Length == 8)
            .Invoke([number, (byte)0, (byte)0, "server", "duplicate key", "procedure", 0, null]);

        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", nonPublic)!.Invoke(errors, [error]);

        return (SqlException)typeof(SqlException)
            .GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, [typeof(SqlErrorCollection), typeof(string)])!
            .Invoke(null, [errors, "16.0"])!;
    }
}
