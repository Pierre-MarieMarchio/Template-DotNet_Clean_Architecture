namespace AppTemplate.Infrastructure.Core.Common.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public int MaxPoolSize { get; set; } = 20;

    public int CommandTimeoutSeconds { get; set; } = 30;
}
