using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Versioning;

public sealed class VersionStampingSaveChangesInterceptor : SaveChangesInterceptor
{
    public const string VersionPropertyName = "Version";

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.FindProperty(VersionPropertyName) is not { IsConcurrencyToken: true } property
                || property.ClrType != typeof(uint))
            {
                continue;
            }

            var version = entry.Property(property);

            switch (entry.State)
            {
                case EntityState.Added:
                    version.CurrentValue = 1u;
                    break;

                case EntityState.Modified:
                    version.CurrentValue = unchecked((uint)version.OriginalValue! + 1u);
                    break;

                default:
                    break;
            }
        }
    }
}
