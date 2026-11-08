namespace AppTemplate.Api.Common.Hosting;

internal sealed record DevelopmentDatabaseStep(string Name, Func<IServiceProvider, CancellationToken, Task> RunAsync);
