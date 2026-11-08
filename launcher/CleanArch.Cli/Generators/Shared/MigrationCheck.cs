namespace CleanArch.Cli.Generators.Shared;

internal static class MigrationCheck
{
    internal static string? Refusal(string root, TextWriter output, IExecutor executor, string migration)
    {
        var check = TextEdits.ToolCommand(root, "migrations", "add", "--check");

        output.WriteLine($"Checking that no model change waits for a migration: {check.Display}");
        output.Flush();

        return executor.Run(check) == 0
            ? null
            : $"`{check.Display}` failed: the business model holds a change no migration holds yet, or it could not be checked. "
                + $"{migration} would carry that change too, so put it in a migration of its own with "
                + $"`{Tool.Name} migrations add <Name>`, or undo it, then run this again.";
    }
}
