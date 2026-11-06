namespace CleanArch.Cli.Generators.Shared;

internal static class Refusal
{
    internal const int Code = 1;

    internal static int Write(TextWriter error, IEnumerable<string> problems, string usage)
    {
        foreach (string problem in problems)
        {
            error.WriteLine($"error: {problem}");
        }

        error.WriteLine();
        error.WriteLine(usage);
        error.WriteLine();
        error.WriteLine("Nothing was written.");

        return Code;
    }
}
