using CA.Application.Commons.Interfaces.Services;
using CA.Domain.Commons.Bases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CA.Infrastructure.Commons.Extensions;

public static class EntityFrameworkExtensions
{

    public static void ApplyAuditing(this ChangeTracker changeTracker, IAuthenticatedUserService authenticatedUser)
    {
        var userId = string.IsNullOrEmpty(authenticatedUser.UserId)
            ? Guid.Empty : Guid.Parse(authenticatedUser.UserId);

        var currentTime = DateTime.UtcNow;

        foreach (var entry in changeTracker.Entries())
        {
            var entityType = entry.Entity.GetType();

            if (typeof(AuditableBaseEntity).IsAssignableFrom(entityType) ||
                (entityType.BaseType?.IsGenericType ?? false) &&
                entityType.BaseType.GetGenericTypeDefinition() == typeof(AuditableBaseEntity<>))
            {
                dynamic auditableEntity = entry.Entity;

                if (entry.State == EntityState.Added)
                {
                    auditableEntity.CreatedBy = userId;
                    auditableEntity.CreatedAt = currentTime;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditableEntity.LastModifiedBy = userId;
                    auditableEntity.ModifiedAt = currentTime;
                }
            }
        }
    }
}
