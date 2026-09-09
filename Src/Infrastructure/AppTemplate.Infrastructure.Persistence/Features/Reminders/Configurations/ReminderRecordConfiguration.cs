using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Configurations;

internal sealed class ReminderRecordConfiguration : IEntityTypeConfiguration<ReminderRecord>
{
    public void Configure(EntityTypeBuilder<ReminderRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Reminders", AppDbContext.RemindersSchema);

        builder.HasKey(reminder => reminder.Id);

        // Ids are UUIDv7, created by the domain.
        builder.Property(reminder => reminder.Id).ValueGeneratedNever();

        builder.Property(reminder => reminder.OwnerId).IsRequired();
        builder.Property(reminder => reminder.TodoListId).IsRequired();
        builder.Property(reminder => reminder.TodoItemId).IsRequired();
        builder.Property(reminder => reminder.DueAt).IsRequired();
        builder.Property(reminder => reminder.State).IsRequired();

        // "State = Pending AND DueAt <= now", ordered by DueAt: State leads as the equality filter,
        // DueAt trails as both the range predicate and the sort key.
        builder.HasIndex(reminder => new { reminder.State, reminder.DueAt })
            .HasDatabaseName("IX_Reminders_State_DueAt");

        // Every reminder for one item, whatever its state, for when the item is completed or removed.
        builder.HasIndex(reminder => reminder.TodoItemId)
            .HasDatabaseName("IX_Reminders_TodoItemId");

        // PostgreSQL's xmin system column, which already exists: EF reads it back after a write and
        // puts it in the WHERE clause of the next one.
        builder.Property(reminder => reminder.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(reminder => reminder.CreatedAt).IsRequired();
        builder.Property(reminder => reminder.CreatedBy);
        builder.Property(reminder => reminder.LastModifiedAt);
        builder.Property(reminder => reminder.LastModifiedBy);
    }
}
