using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.DomainEvents;

/// <summary>
/// The event-data arguments EF hands a save-changes interceptor.
/// </summary>
/// <remarks>
/// The definition, the message generator and the context are all left null: an interceptor that collects
/// and publishes domain events reads none of them, and building a real <c>EventDefinition</c> would drag
/// in the whole logging infrastructure to describe a message no test ever renders. A future interceptor
/// that did read them would fail here loudly rather than pass on a fixture that lied.
/// </remarks>
internal static class ASaveChangesEvent
{
    internal static DbContextEventData Saving() => new(null!, null!, context: null);

    internal static SaveChangesCompletedEventData Saved(int rowsAffected) =>
        new(null!, null!, context: null!, rowsAffected);

    internal static DbContextErrorEventData Failed(Exception exception) =>
        new(null!, null!, context: null!, exception);
}
