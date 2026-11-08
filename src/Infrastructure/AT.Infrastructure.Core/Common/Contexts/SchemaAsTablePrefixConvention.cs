using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

public sealed class SchemaAsTablePrefixConvention : IModelFinalizingConvention
{
    public static string TableName(string schema, string table) => $"{schema}_{table}";

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().Where(entityType => !entityType.IsOwned()))
        {
            if (entityType.GetSchema() is { } schema && entityType.GetTableName() is { } table)
            {
                entityType.SetTableName(TableName(schema, table));
                entityType.SetSchema(null);
            }
        }

        modelBuilder.Metadata.SetDefaultSchema(null);
    }
}
