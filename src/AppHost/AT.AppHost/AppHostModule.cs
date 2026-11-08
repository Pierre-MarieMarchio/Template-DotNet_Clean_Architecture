using AppTemplate.AppHost.Common.Hosting;

namespace AppTemplate.AppHost;

public static class AppHostModule
{
    public static async Task Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        builder.AddLocalStack();

        await builder.Build().RunAsync();
    }

    public static IDistributedApplicationBuilder AddLocalStack(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        new LocalStack(builder, ProjectFile.Above(builder.AppHostDirectory)).AddHosts();

        return builder;
    }
}
