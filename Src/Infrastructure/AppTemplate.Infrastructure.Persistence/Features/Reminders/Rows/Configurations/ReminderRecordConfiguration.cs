using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Configurations;

internal sealed class ReminderRecordConfiguration : IEntityTypeConfiguration<ReminderRecord>
{
    public void Configure(EntityTypeBuilder<ReminderRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Reminders", AppDbContext.RemindersSchema);

        builder.HasKey(reminder => reminder.Id);

        builder.Property(reminder => reminder.Id).ValueGeneratedNever();

        builder.Property(reminder => reminder.OwnerId).IsRequired();
        builder.Property(reminder => reminder.TodoListId).IsRequired();
        builder.Property(reminder => reminder.TodoItemId).IsRequired();
        builder.Property(reminder => reminder.DueAt).IsRequired();
        builder.Property(reminder => reminder.State).IsRequired();
        builder.Property(reminder => reminder.FailedAttempts).IsRequired();

        builder.HasIndex(reminder => new { reminder.State, reminder.DueAt })
            .HasDatabaseName("IX_Reminders_State_DueAt");

        builder.HasIndex(reminder => reminder.TodoItemId)
            .HasDatabaseName("IX_Reminders_TodoItemId");

        builder.Property(reminder => reminder.Version).IsVersion();

        builder.Property(reminder => reminder.CreatedAt).IsRequired();
        builder.Property(reminder => reminder.CreatedBy);
        builder.Property(reminder => reminder.LastModifiedAt);
        builder.Property(reminder => reminder.LastModifiedBy);
    }
}
