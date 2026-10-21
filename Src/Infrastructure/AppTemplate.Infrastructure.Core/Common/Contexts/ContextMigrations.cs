namespace AppTemplate.Infrastructure.Core.Common.Contexts;

public sealed record ContextMigrations(string Assembly, string HistoryTable, string HistorySchema);
