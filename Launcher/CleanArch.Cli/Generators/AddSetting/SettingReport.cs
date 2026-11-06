using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddSetting;

internal static class SettingReport
{
    internal static void Write(TextWriter output, SettingSpec spec, IReadOnlyList<Emission> emissions, IEnumerable<string> edited, List<string> hosts, bool dryRun)
    {
        string verb = dryRun ? "Would write" : "Wrote";

        output.WriteLine(dryRun ? "Dry run: nothing was written." : $"Wrote the {spec.Section} setting of {spec.Features}.");
        output.WriteLine();
        output.WriteLine($"{verb} {emissions.Count} file(s):");

        foreach (var emission in emissions)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();
        output.WriteLine($"{(dryRun ? "Would edit" : "Edited")}:");

        foreach (string path in edited.Order(StringComparer.Ordinal))
        {
            output.WriteLine($"  {path}");
        }

        output.WriteLine();
        output.WriteLine($"Hosts whose appsettings.json carries the section: {string.Join(", ", hosts)}.");
        output.WriteLine();
        output.WriteLine("What is left to you:");
        output.WriteLine($"  - docs/reference/configuration.md: say, in each Notes cell of the {spec.Section} table, what the key changes; the bounds are written.");
        output.WriteLine($"  - Inject IOptions<{spec.Options}> where the rule lives: the command's validator for a bound on a request, the use case for one that shapes the work.");

        foreach (var key in spec.Keys.Where(key => key.IsRequired))
        {
            output.WriteLine($"  - {key.Name} has no value in the file: `dotnet user-secrets set {spec.Section}:{key.Name} <value>` locally, or {spec.Variable(key)} in .env for Compose, whose .env.example now lists it empty;");
            output.WriteLine($"    the host refuses to start without it, and so does any test that starts a host: give it a value there, as ValidSettings in HostComposition does for the architecture tests.");
        }
    }
}
