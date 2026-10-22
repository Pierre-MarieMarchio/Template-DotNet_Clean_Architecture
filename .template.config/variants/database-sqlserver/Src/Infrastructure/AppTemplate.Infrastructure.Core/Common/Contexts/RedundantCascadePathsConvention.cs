using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

// SQL Server refuses a foreign key that gives a table two cascade paths from one principal.
// A cascade the same rows already follow through a longer path is dropped, so nothing outlives its principal.
public sealed class RedundantCascadePathsConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var model = (IMutableModel)modelBuilder.Metadata;
        var cascades = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetDeclaredForeignKeys())
            .Where(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Cascade && !foreignKey.IsOwnership)
            .OrderBy(foreignKey => foreignKey.DeclaringEntityType.Name, StringComparer.Ordinal)
            .ThenBy(foreignKey => foreignKey.PrincipalEntityType.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var foreignKey in cascades.ToList())
        {
            cascades.Remove(foreignKey);

            if (IsReachable(cascades, foreignKey.PrincipalEntityType, foreignKey.DeclaringEntityType))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
            }
            else
            {
                cascades.Add(foreignKey);
            }
        }
    }

    private static bool IsReachable(List<IMutableForeignKey> cascades, IMutableEntityType from, IMutableEntityType to)
    {
        var visited = new HashSet<IMutableEntityType> { from };
        var pending = new Stack<IMutableEntityType>([from]);

        while (pending.TryPop(out var current))
        {
            foreach (var next in cascades
                .Where(foreignKey => foreignKey.PrincipalEntityType == current)
                .Select(foreignKey => foreignKey.DeclaringEntityType))
            {
                if (next == to)
                {
                    return true;
                }

                if (visited.Add(next))
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }
}
