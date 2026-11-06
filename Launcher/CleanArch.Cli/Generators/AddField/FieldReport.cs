using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.CSharpNames;
using static CleanArch.Cli.Generators.Shared.FeatureCode;

namespace CleanArch.Cli.Generators.AddField;

internal static class FieldReport
{
    internal static void Write(TextWriter output, string root, Addition addition, List<FieldChange> changes, ReportMode mode)
    {
        var spec = addition.After;
        string verb = mode == ReportMode.Applied ? "Added" : "Would add";

        output.WriteLine(
            $"{verb} {string.Join(", ", addition.Added.Select(field => field.Declared))} to {spec.Features}/{spec.Type} "
            + $"(access {spec.Access}, model {spec.Model}, operations {string.Join(',', spec.Operations)}, concurrency {spec.Concurrency}"
            + (spec.Access == "owner" ? $", deletion {spec.Deletion}" : string.Empty) + "), "
            + $"{(mode == ReportMode.Applied ? "changing" : "changes")} {changes.Count} file(s), each through its syntax tree:");

        foreach (var change in changes)
        {
            output.WriteLine($"  {change.Path}");

            foreach (string member in change.Members)
            {
                output.WriteLine($"      {member}");
            }
        }

        output.WriteLine();
        output.WriteLine(
            $"Manifest: {ManifestPath(spec)}{(mode == ReportMode.Applied ? string.Empty : ", not written")}: the fields join its fields, with each "
            + "default, its files their new SHA-256, and addedFields the declaration, the migration and each file's change, "
            + "with its SHA-256 before and after.");

        output.WriteLine();
        output.WriteLine("Each field is checked by the validator and by the aggregate or the service, stored in a column of its length or");
        output.WriteLine("precision, and carried by the contracts and the generated tests, as --fields writes it.");

        foreach (var field in addition.Added)
        {
            output.WriteLine();
            output.WriteLine(Compatibility(spec, field));
        }

        if (spec.Has("list") && addition.Added.Where(FieldCode.IsSortable).Select(field => field.Name).ToList() is { Count: > 0 } sortable)
        {
            output.WriteLine();
            output.WriteLine($"The list now sorts by {string.Join(", ", sortable)} too, each with its index, which the migration creates.");
        }

        if (mode != ReportMode.Applied)
        {
            output.WriteLine();
            output.WriteLine(mode == ReportMode.DryRun
                ? "Then a run without --dry-run checks that no model change waits for a migration, writes, and runs in this order, stopping at the first that fails:"
                : "Then, once the changes are made:");

            foreach (var step in FieldGeneration.FieldSteps(root, addition))
            {
                output.WriteLine($"  {step.Display}");
            }
        }
    }

    internal static string Compatibility(FeatureSpec spec, Field field)
    {
        string route = $"/api/v1/{Kebab(spec.Features)}";
        string item = $"{route}/{{{CSharpNames.Lower(spec.Type)}Id}}";
        string[] writes =
        [
            .. spec.Has("create") ? [spec.HasApi ? $"POST {route}" : "the create operation"] : Array.Empty<string>(),
            .. spec.Has("update") ? [spec.HasApi ? $"PUT {item}" : "the update operation"] : Array.Empty<string>(),
        ];

        if (writes.Length == 0)
        {
            return $"{field.Name}: only read, so it adds a member to what the feature returns and breaks no caller.";
        }

        if (!field.Nullable)
        {
            return spec.HasApi
                ? $"Breaking change: {field.Name} is required, so {string.Join(" and ", writes)} answer 400 to a request without it from now "
                    + "on, and every client that does not send it yet breaks. Release the clients with the API, or declare the field "
                    + $"nullable ({(field with { Nullable = true }).Declaration}), which breaks none. The rows already stored take {field.Default!.Literal}."
                : $"Breaking change: {field.Name} is required, so every caller of {string.Join(" and ", writes)} passes it from now on. "
                    + $"The rows already stored take {field.Default!.Literal}.";
        }

        return $"{field.Name} is nullable: a {(spec.HasApi ? "request" : "caller")} may leave it out, so none breaks; the rows already stored hold null"
            + (spec.Has("update")
                ? $". An update replaces the whole {Separated(spec.Type, ' ')}, so one that leaves {field.Name} out stores null: a client that does not know the field yet clears it."
                : ".");
    }
}
