namespace AppTemplate.Api.Common.Jobs;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    public bool Enabled { get; set; } = true;
}
