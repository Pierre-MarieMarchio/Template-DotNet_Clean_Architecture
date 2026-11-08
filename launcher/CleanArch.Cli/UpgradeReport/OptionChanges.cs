namespace CleanArch.Cli.UpgradeReport;

internal static class OptionChanges
{
    public static IEnumerable<string> Between(TemplatePackage older, TemplatePackage newer, Provenance project, string version)
    {
        HashSet<string> used = [PackagedTemplate.Main, .. GenerationPlan.Of(project).Replays.Select(replay => replay.Template)];

        foreach (string identity in older.Templates.Keys.Union(newer.Templates.Keys).Order(StringComparer.Ordinal))
        {
            PackagedTemplate? before = older.Templates.GetValueOrDefault(identity);
            PackagedTemplate? after = newer.Templates.GetValueOrDefault(identity);

            if (before is null || after is null)
            {
                yield return $"{(after ?? before)!.ShortName}: version {version} {(after is null ? "no longer holds" : "adds")} this template.";
            }
            else if (used.Contains(identity))
            {
                bool recordsOptions = identity == PackagedTemplate.Main;
                foreach (string change in Of(before, after, recordsOptions ? project : null, version))
                {
                    yield return change;
                }
            }
        }
    }

    private static IEnumerable<string> Of(PackagedTemplate before, PackagedTemplate after, Provenance? recording, string version)
    {
        Dictionary<string, TemplateParameter> was = before.ParametersByName();
        Dictionary<string, TemplateParameter> @is = after.ParametersByName();
        string[] removed = [.. was.Keys.Except(@is.Keys).Order(StringComparer.Ordinal)];
        string[] added = [.. @is.Keys.Except(was.Keys).Order(StringComparer.Ordinal)];

        IEnumerable<string> removals = removed.Select(name => Removal(before, after, name, added, recording, version));
        IEnumerable<string> additions = added.Select(name =>
            $"{after.ShortName}: version {version} adds the option {after.Option(name)}, generated here under its default"
            + $" ({@is[name].Default ?? "none"}): decide by hand whether this project takes another value.");
        IEnumerable<string> changes = was.Keys.Intersect(@is.Keys).Order(StringComparer.Ordinal)
            .SelectMany(name => Changed(after, name, was[name], @is[name], version));

        return removals.Concat(additions).Concat(changes);
    }

    private static string Removal(PackagedTemplate before, PackagedTemplate after, string name, string[] added, Provenance? recording, string version)
    {
        string recorded = recording is not null && recording.Options.Any(option => option.Key == name) ? ", which cleanarch.json records" : string.Empty;

        return $"{after.ShortName}: version {version} removes the option {before.Option(name)}{recorded}"
            + (added.Length > 0 ? $"; it adds {string.Join(", ", added.Select(after.Option))}, and a rename is never assumed" : string.Empty)
            + ": decide by hand.";
    }

    private static IEnumerable<string> Changed(PackagedTemplate after, string name, TemplateParameter was, TemplateParameter @is, string version)
    {
        string[] gone = [.. was.Choices.Except(@is.Choices)];
        string[] come = [.. @is.Choices.Except(was.Choices)];

        if (gone.Length > 0)
        {
            yield return $"{after.ShortName}: version {version} no longer offers {string.Join(", ", gone)} for {after.Option(name)}: decide by hand.";
        }

        if (come.Length > 0)
        {
            yield return $"{after.ShortName}: version {version} offers {string.Join(", ", come)} for {after.Option(name)} as well.";
        }

        if (was.Default != @is.Default)
        {
            yield return $"{after.ShortName}: version {version} changes the default of {after.Option(name)} from {was.Default ?? "none"} to {@is.Default ?? "none"}.";
        }
    }
}
