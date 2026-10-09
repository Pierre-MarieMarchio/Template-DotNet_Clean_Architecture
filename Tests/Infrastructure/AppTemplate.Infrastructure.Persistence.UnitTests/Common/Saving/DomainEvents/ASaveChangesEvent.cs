using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving.DomainEvents;

/// <summary>
/// The event-data arguments EF hands a save-changes interceptor, for a save of one context.
/// </summary>
/// <remarks>
/// The definition and the message generator are left null: an interceptor that collects and publishes
/// domain events reads neither, and building a real <c>EventDefinition</c> would drag in the whole
/// logging infrastructure to describe a message no test ever renders. The context is real, because
/// which context is being saved decides which events that save may take.
/// </remarks>
internal static class ASaveChangesEvent
{
    internal static DbContextEventData Saving(DbContext context) => new(null!, null!, context);

    internal static SaveChangesCompletedEventData Saved(DbContext context, int rowsAffected) =>
        new(null!, null!, context, rowsAffected);

    internal static DbContextErrorEventData Failed(DbContext context, Exception exception) =>
        new(null!, null!, context, exception);
}
