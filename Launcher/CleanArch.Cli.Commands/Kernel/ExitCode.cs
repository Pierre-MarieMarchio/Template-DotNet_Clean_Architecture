namespace CleanArch.Cli.Commands.Kernel;

internal static class ExitCode
{
    public const int Succeeded = 0;
    public const int Failed = 1;

    public static int FirstFailureOf(IEnumerable<Func<int>> steps)
    {
        foreach (Func<int> step in steps)
        {
            int exit = step();
            if (exit != Succeeded)
            {
                return exit;
            }
        }

        return Succeeded;
    }
}
