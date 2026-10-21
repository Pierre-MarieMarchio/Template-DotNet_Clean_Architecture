using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving.Auditing;

public sealed class AuditingSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset _now = new(2026, 6, 7, 8, 9, 10, TimeSpan.Zero);

    private static readonly Guid _userId = new("22222222-2222-2222-2222-222222222222");

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

    private static void Save(AppDbContext context, IAuditActor actor)
    {
        var interceptor = new AuditingSaveChangesInterceptor(actor, new FixedClock());

        interceptor.SavingChanges(new DbContextEventData(null!, null!, context), default);
    }

    private static AppDbContext AContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
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
