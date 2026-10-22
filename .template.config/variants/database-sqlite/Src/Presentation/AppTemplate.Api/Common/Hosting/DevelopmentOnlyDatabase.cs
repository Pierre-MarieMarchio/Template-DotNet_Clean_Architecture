namespace AppTemplate.Api.Common.Hosting;

internal static class DevelopmentOnlyDatabase
{
    internal const string Refusal =
        "This project runs on SQLite, a development database: generate it again with --database postgresql, "
        + "sqlserver or mysql to deploy it.";

    internal static void RefuseOutsideDevelopment(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(Refusal);
        }
    }
}
