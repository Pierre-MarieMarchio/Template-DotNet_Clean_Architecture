using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal sealed class FileSameness(Func<string, bool> binary, IReadOnlySet<string>? heldGuids)
{
    public static FileSameness For(string projectRoot, IReadOnlySet<string>? heldGuids) => new(BinaryPatterns(projectRoot), heldGuids);

    public bool Same(string left, string right)
    {
        byte[] leftBytes = File.ReadAllBytes(left);
        byte[] rightBytes = File.ReadAllBytes(right);

        if (leftBytes.AsSpan().SequenceEqual(rightBytes))
        {
            return true;
        }

        if (binary(left) || CanonicalText.IsBinary(leftBytes) || CanonicalText.IsBinary(rightBytes))
        {
            return false;
        }

        return SameText(left, CanonicalText.Normalised(leftBytes), CanonicalText.Normalised(rightBytes));
    }

    private bool SameText(string path, string left, string right)
    {
        if (left == right)
        {
            return true;
        }

        if (path.EndsWith(".sln", StringComparison.Ordinal))
        {
            return CanonicalText.Solution(left) == CanonicalText.Solution(right);
        }

        if (heldGuids is not null)
        {
            (left, right) = (CanonicalText.WithoutDrawnGuids(left, heldGuids), CanonicalText.WithoutDrawnGuids(right, heldGuids));
        }

        return left == right
            || (path.EndsWith(".cs", StringComparison.Ordinal) && CanonicalText.CSharp(left) == CanonicalText.CSharp(right));
    }

    private static Func<string, bool> BinaryPatterns(string root)
    {
        string attributes = Path.Combine(root, ".gitattributes");
        Regex[] patterns = File.Exists(attributes)
            ? [.. File.ReadAllLines(attributes)
                .Select(line => line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Where(fields => fields.Length > 1 && !fields[0].StartsWith('#') && !fields[0].Contains('/') && fields.Skip(1).Any(field => field is "binary" or "-text"))
                .Select(fields => new Regex("^" + Regex.Escape(fields[0]).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$", RegexOptions.None, TimeSpan.FromSeconds(2)))]
            : [];

        return path => patterns.Any(pattern => pattern.IsMatch(Path.GetFileName(path)));
    }
}
