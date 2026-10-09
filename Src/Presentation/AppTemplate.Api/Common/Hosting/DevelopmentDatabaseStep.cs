namespace AppTemplate.Api.Common.Hosting;

/// <summary>
/// One module's part of the development bootstrap: migrating its own context, and seeding it if it
/// seeds. A module registers its step with its own composition, so leaving the module out leaves
/// its schema out of the bootstrap too.
/// </summary>
/// <param name="Name">Named in the log line that reports a failed step.</param>
/// <param name="RunAsync">Runs in the bootstrap's scope, after the business schema is migrated.</param>
internal sealed record DevelopmentDatabaseStep(string Name, Func<IServiceProvider, CancellationToken, Task> RunAsync);
