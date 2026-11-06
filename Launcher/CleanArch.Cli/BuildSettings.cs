using System.CommandLine;

namespace CleanArch.Cli;

internal sealed record BuildSettings(string Configuration, int? MaxCpuCount)
{
    public string[] Nodes => MaxCpuCount is { } count ? [$"-maxcpucount:{count}"] : [];

    public string[] Modules => MaxCpuCount is { } count ? ["--max-parallel-test-modules", count.ToString(System.Globalization.CultureInfo.InvariantCulture)] : [];

    public static Options Add(Command command)
    {
        Option<string> configuration = new("--configuration", "-c")
        {
            Description = "The build configuration, Debug or Release.",
            DefaultValueFactory = _ => "Debug",
        };
        Option<int?> maxCpuCount = new("-m", "-maxcpucount")
        {
            Description = "Cap the build's nodes and the test modules run at once, as in -m:2, on a machine short of memory.",
        };
        maxCpuCount.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is < 1)
            {
                result.AddError("-m needs a whole number of at least 1, as in -m:2.");
            }
        });
        command.Options.Add(configuration);
        command.Options.Add(maxCpuCount);
        return new Options(configuration, maxCpuCount);
    }

    public sealed record Options(Option<string> Configuration, Option<int?> MaxCpuCount)
    {
        public BuildSettings Of(ParseResult parseResult) =>
            new(parseResult.GetRequiredValue(Configuration), parseResult.GetValue(MaxCpuCount));
    }
}
