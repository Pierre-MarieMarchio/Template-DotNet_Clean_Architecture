using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving.Auditing;

/// <summary>
/// The interceptor runs on every save from every host, so what it asks for decides which hosts can
/// write at all. It asks <see cref="IAuditActor"/>, which a host with no principal can answer, and
/// not <see cref="ICurrentUser"/>, which such a host cannot.
/// <para>
/// No database is contacted: the provider is only there so the model exists, and change tracking
/// needs no connection.
/// </para>
/// </summary>
public sealed class AuditingSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset _now = new(2026, 6, 7, 8, 9, 10, TimeSpan.Zero);

    private static readonly Guid _userId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// The case that broke <c>AppTemplate.Worker</c>: an actor naming nobody must produce a stamped
    /// row, not an exception. Every background loop commits through here, so an actor this
    /// interceptor cannot consult is a host that can write nothing.
    /// </summary>
    [Fact]
    public void AnActorNamingNobody_StampsTheRowWithoutAUser()
    {
        using var context = AContext();
        var record = new TodoListRecord { Id = Guid.NewGuid(), OwnerId = _userId, Name = "Groceries" };
        context.Add(record);

        Save(context, new StubAuditActor(null));

        record.CreatedAt.ShouldBe(_now);
        record.CreatedBy.ShouldBeNull("nobody is a fact about a background write, not a missing value");
    }

    [Fact]
    public void AnActorNamingAUser_StampsThatUser()
    {
        using var context = AContext();
        var record = new TodoListRecord { Id = Guid.NewGuid(), OwnerId = _userId, Name = "Groceries" };
        context.Add(record);

        Save(context, new StubAuditActor(_userId));

        record.CreatedBy.ShouldBe(_userId);
    }

    [Fact]
    public void AModifiedRow_TakesTheLastModifiedStampAndKeepsItsCreatedOne()
    {
        using var context = AContext();
        var record = new TodoListRecord { Id = Guid.NewGuid(), OwnerId = _userId, Name = "Groceries" };
        context.Attach(record);
        context.Entry(record).State = EntityState.Modified;

        Save(context, new StubAuditActor(null));

        record.LastModifiedAt.ShouldBe(_now);
        record.LastModifiedBy.ShouldBeNull();
        record.CreatedAt.ShouldBe(default(DateTimeOffset), "a modification does not re-create the row");
    }

    /// <summary>
    /// Proves the interceptor never reaches for the calling principal. An actor that threw the way
    /// the worker's <c>BackgroundCurrentUser</c> does would surface here rather than on the first
    /// deposited file in production.
    /// </summary>
    [Fact]
    public void TheInterceptor_ReadsTheActorExactlyOncePerSave_AndNothingElse()
    {
        using var context = AContext();
        context.Add(new TodoListRecord { Id = Guid.NewGuid(), OwnerId = _userId, Name = "Groceries" });
        context.Add(new TodoListRecord { Id = Guid.NewGuid(), OwnerId = _userId, Name = "Hardware" });

        var actor = new StubAuditActor(null);

        Save(context, actor);

        actor.Reads.ShouldBe(1, "the stamp is one question per save, not one per row");
    }

    // ---- Fixture -----------------------------------------------------------------------------

    private static void Save(AppDbContext context, IAuditActor actor)
    {
        var interceptor = new AuditingSaveChangesInterceptor(actor, new FixedClock());

        interceptor.SavingChanges(new DbContextEventData(null!, null!, context), default);
    }

    private static AppDbContext AContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never-opened;Username=none;Password=none")
            .Options);

    private sealed class StubAuditActor(Guid? userId) : IAuditActor
    {
        internal int Reads { get; private set; }

        public Guid? UserId
        {
            get
            {
                Reads++;

                return userId;
            }
        }
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => _now;
    }
}
