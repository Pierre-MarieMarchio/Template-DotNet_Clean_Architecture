using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.CSharpNames;

namespace CleanArch.Cli.Generators.AddFeature;

internal static class FeaturePage
{
    internal const string _pageTemplate = "docs/_page-templates/feature.md";

    private static string Concurrency(FeatureSpec spec) => spec switch
    {
        { Concurrency: not "etag" } => ", and the last write wins",
        { HasApi: true } => ", and a write sends If-Match with the ETag a read returned",
        _ => ", and a write carries the version a read returned",
    };

    internal static string PagePath(string features) => $"docs/features/{Kebab(features)}.md";

    internal static Emission? PageOf(string root, FeatureSpec spec, List<Emission> plan, out string? unwritten)
    {
        string path = PagePath(spec.Features);
        string template = System.IO.Path.Combine(root, _pageTemplate);

        if (File.Exists(System.IO.Path.Combine(root, path)))
        {
            unwritten = $"'{path}' already exists, so it was left as it is and the manifest does not record it: remove feature never deletes it.";

            return null;
        }

        if (!File.Exists(template))
        {
            unwritten = $"'{_pageTemplate}' is not in this tree, so '{path}' was not written; write it by hand.";

            return null;
        }

        unwritten = null;

        return new Emission(path, PageText(File.ReadAllText(template), spec, plan));
    }

    internal static string PageText(string template, FeatureSpec spec, List<Emission> plan)
    {
        string plural = Separated(spec.Features, ' ');
        string singular = Separated(spec.Type, ' ');
        string one = (IsVowel(singular[0]) ? "an " : "a ") + singular;
        string route = $"/api/v1/{Kebab(spec.Features)}";
        string item = $"{route}/{{{CSharpNames.Lower(spec.Type)}Id}}";

        string fate = spec.Deletion == "anonymise" ? "anonymised" : "deleted";
        string access = spec.Access == "owner"
            ? $"each authenticated caller, on their own {plural} only; once an account is deleted, its {plural} are {fate}"
            : $"every authenticated caller, on the same {plural}";

        string shape = (spec.Model == "record"
                ? "a service over a store, with no domain model"
                : $"one use case per operation over the {singular} aggregate")
            + Concurrency(spec);

        (string Operation, string Line)[] endpoints = spec.HasApi
            ?
            [
                ("list", $"- `GET {route}`: a page of {plural}"),
                ("get", $"- `GET {item}`: {one}, and `HEAD` for its headers alone"),
                ("create", $"- `POST {route}`: creates {one}"),
                ("update", $"- `PUT {item}`: replaces {one}"),
                ("delete", $"- `DELETE {item}`: deletes {one}"),
            ]
            :
            [
                ("list", $"- list: a page of {plural}"),
                ("get", $"- get: {one}"),
                ("create", $"- create: creates {one}"),
                ("update", $"- update: replaces {one}"),
                ("delete", $"- delete: deletes {one}"),
            ];

        string marker = $"/Features/{spec.Features}";
        string[] tests = [.. plan
            .Select(emission => emission.Path)
            .Where(path => path.StartsWith("Tests/", StringComparison.Ordinal) && path.Contains(marker + "/", StringComparison.Ordinal))
            .Select(path => path[..(path.IndexOf(marker + "/", StringComparison.Ordinal) + marker.Length)])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        return template
            .Replace("type: template\n", "type: feature\n", StringComparison.Ordinal)
            .Replace("<description>", $"What a caller can do with {plural}, who may, and how the feature works.", StringComparison.Ordinal)
            .Replace("<Feature>", char.ToUpperInvariant(plural[0]) + plural[1..], StringComparison.Ordinal)
            .Replace("<access>", access, StringComparison.Ordinal)
            .Replace("<shape>", shape, StringComparison.Ordinal)
            .Replace("<endpoints>", string.Join('\n', endpoints.Where(endpoint => spec.Has(endpoint.Operation)).Select(endpoint => endpoint.Line)), StringComparison.Ordinal)
            .Replace(
                "<tests>",
                tests.Length == 0 ? "no unit test project of this tree received one" : string.Join(", ", tests.Select(folder => $"`{folder}`")),
                StringComparison.Ordinal);
    }
}
