using Shouldly;

namespace CleanArch.Cli.Tests.Generators;

internal static class Expectations
{
    public static void Hold(Action<string, Action<bool, string>> cases)
    {
        string scratch = Directory.CreateTempSubdirectory("cleanarch-generator-").FullName;
        try
        {
            Hold(expect => cases(scratch, expect));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    public static void Hold(Action<Action<bool, string>> cases)
    {
        var problems = new List<string>();
        cases((condition, message) =>
        {
            if (!condition)
            {
                problems.Add(message);
            }
        });
        problems.ShouldBeEmpty();
    }
}
