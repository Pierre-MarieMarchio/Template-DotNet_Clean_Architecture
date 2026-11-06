using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;

namespace CleanArch.Cli.Generators.Shared;

internal sealed class FeatureManifest
{
    internal const string Folder = ".feature-manifest";

    internal const int EditsVersion = 2;

    private readonly JsonObject _top;

    private readonly SortedDictionary<string, string> _files = new(StringComparer.Ordinal);

    private readonly List<TextEdits.Edit> _edits;

    private readonly int _editsRead;

    private FeatureManifest(string path, JsonObject top, byte[]? original, List<TextEdits.Edit> edits)
    {
        Path = path;
        Original = original;
        _top = top;
        _edits = edits;
        _editsRead = edits.Count;

        foreach (var file in ((JsonArray)top["files"]!).OfType<JsonObject>())
        {
            _files[file["path"]!.GetValue<string>()] = file["sha256"]!.GetValue<string>();
        }
    }

    internal string Path { get; }

    internal byte[]? Original { get; }

    internal int? SchemaVersion =>
        _top["schemaVersion"] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out int version) ? version : null;

    internal string? Generator => Text(_top["generator"]);

    internal string? GeneratorVersion => Text(_top["generatorVersion"]);

    internal string? Feature => Text(_top["feature"]);

    internal string? Type => Text(_top["type"]);

    internal string? Page => Text(_top["page"]);

    internal JsonObject? Options => _top["options"] as JsonObject;

    internal JsonArray? Fields => _top["fields"] as JsonArray;

    internal IReadOnlyDictionary<string, string> Files => _files;

    internal IReadOnlyList<TextEdits.Edit> Edits => _edits;

    internal IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields =>
        [
            .. (_top["addedFields"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(addition => Text(addition["declaration"]) is not null && Text(addition["migration"]) is not null)
                .Select(addition => (
                    Text(addition["declaration"])!,
                    Text(addition["migration"])!,
                    (string[])[.. (addition["files"] as JsonArray ?? []).OfType<JsonObject>().Select(file => Text(file["path"])).OfType<string>()])),
        ];

    internal (string Path, byte[] Original, byte[] Bytes) Replacement => (Path, Original!, Bytes());

    internal static string PathOf(string features) => $"{Folder}/{features}.json";

    internal static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static FeatureManifest Create(string generator, string generatorVersion, string features, string type, JsonObject options, JsonArray? fields, string? page)
    {
        var top = new JsonObject
        {
            ["schemaVersion"] = EditsVersion,
            ["generator"] = generator,
            ["generatorVersion"] = generatorVersion,
            ["feature"] = features,
            ["type"] = type,
            ["options"] = options,
        };

        if (fields is not null)
        {
            top["fields"] = fields;
        }

        top["files"] = new JsonArray();

        if (page is not null)
        {
            top["page"] = page;
        }

        top["edits"] = new JsonArray();

        return new FeatureManifest(PathOf(features), top, null, []);
    }

    internal static FeatureManifest? Read(string root, string features, out string? problem)
    {
        string path = PathOf(features);
        string full = System.IO.Path.Combine(root, path);

        problem = null;

        if (!File.Exists(full))
        {
            return null;
        }

        byte[] original;

        try
        {
            original = File.ReadAllBytes(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }

        try
        {
            if (JsonNode.Parse(original) is not JsonObject top || !IsManifestOf(top, features))
            {
                problem = $"does not read as the manifest of {features}";

                return null;
            }

            if (ReadEdits(top["edits"] as JsonArray ?? []) is not { } read)
            {
                problem = $"does not read as the manifest of {features}: an edit names no file, anchor, position or lines";

                return null;
            }

            return new FeatureManifest(path, top, original, read);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }
    }

    internal void Record(string path, byte[] bytes) => _files[path] = Sha256(bytes);

    internal void Record(TextEdits.Editor editor)
    {
        foreach (var sheet in editor.Changed)
        {
            if (_files.TryGetValue(sheet.Path, out string? recorded) && recorded == Sha256(sheet.Original))
            {
                _files[sheet.Path] = Sha256(sheet.Bytes());
            }
        }

        _edits.AddRange(editor.Applied.Where(edit => !_files.ContainsKey(edit.Path)));
    }

    internal void RecordAddition(string declaration, string migration, IEnumerable<(string Path, byte[]? Before, byte[] After, IReadOnlyList<string> Changes)> files)
    {
        var additions = _top["addedFields"] as JsonArray ?? [];

        additions.Add((JsonNode)new JsonObject
        {
            ["declaration"] = declaration,
            ["migration"] = migration,
            ["files"] = new JsonArray([.. files.Select(file => (JsonNode)new JsonObject
            {
                ["path"] = file.Path,
                ["before"] = file.Before is null ? null : Sha256(file.Before),
                ["after"] = Sha256(file.After),
                ["changes"] = new JsonArray([.. file.Changes.Select(change => (JsonNode)JsonValue.Create(change))]),
            })]),
        });
        _top["addedFields"] = additions;
    }

    internal byte[] Bytes()
    {
        var files = new JsonArray();

        foreach (var (path, sha256) in _files)
        {
            files.Add((JsonNode)new JsonObject { ["path"] = path, ["sha256"] = sha256 });
        }

        _top["files"] = files;

        var edits = new JsonArray();

        foreach (var edit in _edits)
        {
            var written = new JsonObject
            {
                ["path"] = edit.Path,
                ["anchor"] = edit.Anchor,
                ["position"] = edit.Position,
                ["lines"] = new JsonArray([.. edit.Lines.Select(line => (JsonNode)JsonValue.Create(line))]),
            };

            if (edit.Replaced.Length > 0)
            {
                written["replaced"] = new JsonArray([.. edit.Replaced.Select(line => (JsonNode)JsonValue.Create(line))]);
            }

            edits.Add((JsonNode)written);
        }

        if (_edits.Count > _editsRead)
        {
            _top["schemaVersion"] = Math.Max(SchemaVersion ?? 1, EditsVersion);
            _top.Remove("edits");
            _top["edits"] = edits;
        }
        else if (_top.ContainsKey("edits"))
        {
            _top["edits"] = edits;
        }

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            _top.WriteTo(writer);
        }

        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static bool IsManifestOf(JsonObject top, string features) =>
        Text(top["feature"]) == features
        && top["files"] is JsonArray files
        && files.All(file => file is JsonObject entry && Text(entry["path"]) is not null && Text(entry["sha256"]) is not null)
        && (top["edits"] is not { } edits || edits is JsonArray);

    private static List<TextEdits.Edit>? ReadEdits(JsonArray edits)
    {
        var read = new List<TextEdits.Edit>();

        foreach (var node in edits)
        {
            if (ReadEdit(node) is not { } edit)
            {
                return null;
            }

            read.Add(edit);
        }

        return read;
    }

    private static TextEdits.Edit? ReadEdit(JsonNode? node)
    {
        if (node is not JsonObject edit
            || Text(edit["path"]) is not { } path
            || Text(edit["anchor"]) is not { } anchor
            || Text(edit["position"]) is not ("before" or "after") || edit["lines"] is not JsonArray lines
            || !AreTexts(lines)
            || (edit["replaced"] is { } replaced && (replaced is not JsonArray replacedLines || !AreTexts(replacedLines))))
        {
            return null;
        }

        return new TextEdits.Edit(
            path,
            anchor,
            Text(edit["position"])!,
            [.. lines.Select(line => Text(line)!)],
            [.. (edit["replaced"] as JsonArray ?? []).Select(line => Text(line)!)]);
    }

    private static bool AreTexts(JsonArray lines) => lines.All(line => Text(line) is not null);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
