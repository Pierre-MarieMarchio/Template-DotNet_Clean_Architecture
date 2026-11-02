using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving.DomainEvents;

internal static class ASaveChangesEvent
{
    internal static DbContextEventData Saving(DbContext context) => new(null!, null!, context);

    internal static SaveChangesCompletedEventData Saved(DbContext context, int rowsAffected) =>
        new(null!, null!, context, rowsAffected);

    internal static DbContextErrorEventData Failed(DbContext context, Exception exception) =>
        new(null!, null!, context, exception);
}
