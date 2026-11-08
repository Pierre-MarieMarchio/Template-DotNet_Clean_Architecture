using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CleanArch.Cli.Joins;

internal sealed class ProjectRecord
{
    private static readonly string[] _keyOrder = ["template", "name", "options", "client", "web", "presentation"];

    private static readonly string[] _appKeyOrder = ["name", "framework", "audience", "rendering", "port"];

    private static readonly string[] _webKeyOrder = ["scope", "apps", "packages"];

    private static readonly JsonSerializerOptions _written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    private readonly string _path;
    private readonly JsonObject _json;
    private readonly TextWriter _output;

    private ProjectRecord(string path, JsonObject json, TextWriter output)
    {
        _path = path;
        _json = json;
        _output = output;
    }

    public static ProjectRecord Read(string root, TextWriter output)
    {
        string path = Path.Combine(root, ProjectLocator.FileName);
        return JsonNode.Parse(File.ReadAllText(path)) is JsonObject json
            ? new ProjectRecord(path, json, output)
            : throw new JoinRefusedException($"{path} holds no JSON object.");
    }

    public void RecordClient()
    {
        if (_json["client"]?.GetValue<bool>() != true)
        {
            Place(_json, "client", JsonValue.Create(true), _keyOrder);
            Save("the .NET client");
        }
    }

    public void RecordPresentation(PresentationProject presentation)
    {
        if (_json["presentation"] is not JsonArray presentations)
        {
            presentations = [];
            Place(_json, "presentation", presentations, _keyOrder);
        }

        presentations.Add((JsonNode)new JsonObject { ["name"] = presentation.Name, ["framework"] = presentation.Framework });
        Save($"the presentation project {presentation.Name}, of framework {presentation.Framework}");
    }

    public void RecordWebApp(WebApp app, string scope)
    {
        JsonArray apps = WebList(scope, "apps");
        JsonObject? recorded = apps.OfType<JsonObject>().FirstOrDefault(entry => entry["name"]?.GetValue<string>() == app.Name);
        (string Key, JsonNode Value)[] wanted =
        [
            ("name", JsonValue.Create(app.Name)), ("framework", JsonValue.Create(app.Framework)), ("audience", JsonValue.Create(app.Audience)),
            ("rendering", JsonValue.Create(app.Rendering)), ("port", JsonValue.Create(app.Port)!),
        ];

        if (recorded is not null && wanted.All(entry => JsonNode.DeepEquals(recorded[entry.Key], entry.Value)))
        {
            _output.WriteLine($"{ProjectLocator.FileName} records the web app {app.Name} already.");
            return;
        }

        if (recorded is null)
        {
            recorded = [];
            apps.Add((JsonNode)recorded);
        }

        foreach ((string key, JsonNode value) in wanted)
        {
            Place(recorded, key, value, _appKeyOrder);
        }

        string rendered = app.Rendering == "ssr" ? "on the server" : "in the browser";
        Save($"the web app {app.Name}, of framework {app.Framework}, for the {app.Audience} audience, rendered {rendered}, on port {app.Port}");
    }

    public void RecordWebPackage(string name, string scope)
    {
        JsonArray packages = WebList(scope, "packages");
        if (packages.Any(entry => entry?.GetValue<string>() == name))
        {
            _output.WriteLine($"{ProjectLocator.FileName} records the web package {name} already.");
            return;
        }

        packages.Add((JsonNode)JsonValue.Create(name));
        Save($"the web package {name}");
    }

    private JsonArray WebList(string scope, string key)
    {
        if (_json["web"] is not JsonObject web)
        {
            web = new JsonObject { ["scope"] = scope, ["apps"] = new JsonArray(), ["packages"] = new JsonArray() };
            Place(_json, "web", web, _keyOrder);
        }

        if (web[key] is not JsonArray list)
        {
            list = [];
            Place(web, key, list, _webKeyOrder);
        }

        return list;
    }

    private static void Place(JsonObject json, string key, JsonNode? value, string[] order)
    {
        if (json.ContainsKey(key))
        {
            json[key] = value;
            return;
        }

        int index = order.Skip(Array.IndexOf(order, key) + 1)
            .Where(json.ContainsKey)
            .Select(json.IndexOf)
            .DefaultIfEmpty(json.Count)
            .First();
        json.Insert(index, key, value);
    }

    private void Save(string recorded)
    {
        File.WriteAllText(_path, _json.ToJsonString(_written) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _output.WriteLine($"{ProjectLocator.FileName} records {recorded} now.");
    }
}
