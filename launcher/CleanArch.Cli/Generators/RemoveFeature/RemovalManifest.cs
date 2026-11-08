using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class RemovalManifest
{
    internal static Manifest? ReadManifest(string root, string features, List<string> blockers)
    {
        if (FeatureManifest.Read(root, features, out string? problem) is not { } read)
        {
            blockers.Add($"The manifest {problem}.");

            return null;
        }

        if (read.SchemaVersion is not (1 or 2))
        {
            blockers.Add("The manifest has a schema version this command does not read.");

            return null;
        }

        string type = read.Type ?? string.Empty;
        var files = new List<Entry>();
        var shared = new List<string>();
        var consumers = new List<string>();

        if (!ReadEntries(read, features, files, shared, consumers, blockers))
        {
            return null;
        }

        if (type.Length == 0 || files.Count == 0)
        {
            blockers.Add("The manifest names no type or no file.");

            return null;
        }

        if (read.Page is { } page && page != FeatureRemoval.PagePath(features))
        {
            blockers.Add($"The manifest lists the page '{page}', where the generator writes '{FeatureRemoval.PagePath(features)}'.");

            return null;
        }

        string[] outside = [.. read.Edits.Select(edit => edit.Path).Where(path => path.Contains("..", StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(path)).Distinct(StringComparer.Ordinal)];

        if (outside.Length > 0)
        {
            blockers.Add($"The manifest records an edit in '{outside[0]}', which is not a path inside the tree.");

            return null;
        }

        return new Manifest(type, files, shared, consumers, read.Page, read.Edits, read.AddedFields);
    }

    private static bool ReadEntries(FeatureManifest read, string features, List<Entry> files, List<string> shared, List<string> consumers, List<string> blockers)
    {
        var pattern = new Regex($"^(src|tests)/(.+/)?Features/{features}/.+$", RegexOptions.None, FeatureRemoval._timeout);
        var events = read.Files.Keys
            .Where(path => pattern.IsMatch(path) && path.EndsWith("DomainEvent.cs", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path)[..^"DomainEvent".Length])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (entry, digest) in read.Files)
        {
            bool isShared = FeatureRemoval._sharedPaths.Any(sharedPath => sharedPath.IsMatch(entry));
            bool isConsumer = FeatureRemoval._consumer.Match(entry) is { Success: true } consumer && events.Contains(consumer.Groups["base"].Value);

            if (IsStray(entry, digest, pattern.IsMatch(entry) || isShared || isConsumer))
            {
                blockers.Add(
                    $"The manifest lists '{entry}', which is not a file under a Features/{features} folder of src or tests, nor one of the account-deletion files every owner feature shares, "
                    + $"nor the consumer of an event of {features} that add event wrote in another feature.");

                return false;
            }

            files.Add(new Entry(entry, digest));

            if (isShared)
            {
                shared.Add(entry);
            }
            else if (isConsumer)
            {
                consumers.Add(entry);
            }
        }

        return true;
    }

    private static bool IsStray(string entry, string digest, bool belongs) =>
        entry.Contains("..", StringComparison.Ordinal) || entry.Contains('\\', StringComparison.Ordinal) || !belongs || digest.Length != 64;
}
