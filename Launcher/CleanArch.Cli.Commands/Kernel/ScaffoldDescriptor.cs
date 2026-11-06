using System.Text.Json.Nodes;

namespace CleanArch.Cli.Commands.Kernel;

internal sealed class ScaffoldDescriptor
{
    public const string FileName = "scaffold.json";

    private readonly JsonObject _json;
    private readonly string _owner;

    private ScaffoldDescriptor(JsonObject json, string owner)
    {
        _json = json;
        _owner = owner;
    }

    public static bool In(string folder) => File.Exists(PathIn(folder));

    public static ScaffoldDescriptor Read(string folder) => new(ScaffoldJson.Read(PathIn(folder)), Path.GetFileName(folder));

    public static void Delete(string folder) => File.Delete(PathIn(folder));

    public string Kind => _json["kind"]?.GetValue<string>() ?? "pnpm";

    public string Name => Text("name");

    public string Template => Text("template");

    public string Generator => Text("generator");

    public string Package => Text("package") + "@" + Text("version");

    public IReadOnlyList<string> Keep => Strings("keep");

    public IReadOnlyList<string> Remove => Strings("remove");

    public JsonObject Merge => _json["merge"]?.AsObject() ?? [];

    public IEnumerable<string> Arguments(string name) =>
        Strings("arguments").Select(argument => argument
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{directory}", name, StringComparison.Ordinal));

    private string Text(string key) => ScaffoldJson.Text(_json, key, _owner);

    private string[] Strings(string key) => [.. (_json[key]?.AsArray() ?? []).Select(item => item?.GetValue<string>() ?? string.Empty)];

    private static string PathIn(string folder) => Path.Combine(folder, FileName);
}
