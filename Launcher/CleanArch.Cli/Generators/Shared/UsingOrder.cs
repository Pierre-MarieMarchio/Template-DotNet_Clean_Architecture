namespace CleanArch.Cli.Generators.Shared;

internal static class UsingOrder
{
    internal static int Compare(string left, string right)
    {
        int system = IsSystem(right).CompareTo(IsSystem(left));

        if (system != 0)
        {
            return system;
        }

        int ignoringCase = StringComparer.OrdinalIgnoreCase.Compare(left, right);

        return ignoringCase != 0 ? ignoringCase : StringComparer.Ordinal.Compare(left, right);
    }

    private static bool IsSystem(string space) => space == "System" || space.StartsWith("System.", StringComparison.Ordinal);
}
