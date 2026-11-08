using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddUseCase;

internal static class UseCaseHttp
{
    internal static readonly Regex _controllerRoute = new(@"^\[Route\(""(?<template>[^""]*)""\)\]", RegexOptions.Multiline, TimeSpan.FromSeconds(2));

    internal static readonly Regex _actionRoute = new(@"\[Http(?:Get|Post|Put|Patch|Delete|Head)\(""(?<template>[^""]*)""", RegexOptions.None, TimeSpan.FromSeconds(2));

    internal static readonly Regex _rootRoute = new(@"v\{version(?::\w+)?\}/?$", RegexOptions.None, TimeSpan.FromSeconds(2));

    internal static readonly Regex _versionSegment = new(@"v\{version(?::\w+)?\}", RegexOptions.None, TimeSpan.FromSeconds(2));

    internal static string ApiFolder(UseCaseSpec spec) => $"{Presentation}/{FolderOf(spec.Prefix, "Api")}";

    internal static string RequestPath(UseCaseSpec spec) => $"{ApiFolder(spec)}/Features/{spec.Features}/Contracts/Requests/{spec.Request}.cs";

    internal static string ResponseName(UseCaseSpec spec) => spec.Dto![..^"Dto".Length] + "Response";

    internal static string ResponsePath(UseCaseSpec spec) => $"{ApiFolder(spec)}/Features/{spec.Features}/Contracts/Responses/{ResponseName(spec)}.cs";

    internal static string[] ControllerUsings(UseCaseSpec spec) =>
    [
        spec.Namespace,
        "Mediator",
        .. spec.HasRequest ? [$"{spec.Prefix}.Api.Features.{spec.Features}.Contracts.Requests"] : Array.Empty<string>(),
        .. spec.IsCommand
            ? Array.Empty<string>()
            : [$"{spec.Prefix}.Application.Core.Common.Results", $"{spec.Prefix}.Api.Features.{spec.Features}.Contracts.Responses"],
    ];

    internal static string[] RequestContract(UseCaseSpec spec)
    {
        string carrier = spec.IsCommand ? "body" : "query string";
        string summary = spec.HasId
            ? $"The {carrier} of {CSharpNames.Separated(spec.Name, ' ')}; the {CSharpNames.Separated(spec.Aggregate!, ' ')} id travels in the route."
            : $"The {carrier} of {CSharpNames.Separated(spec.Name, ' ')}.";

        return
        [
            $"namespace {spec.Prefix}.Api.Features.{spec.Features}.Contracts.Requests;",
            string.Empty,
            $"/// <summary>{summary}</summary>",
            .. UseCasePlan.Record(spec.Request, spec.Inputs.Select(parameter => $"{parameter.Type} {parameter.Name}"), string.Empty).Split('\n'),
        ];
    }

    internal static string[] ResponseContract(UseCaseSpec spec) =>
    [
        $"namespace {spec.Prefix}.Api.Features.{spec.Features}.Contracts.Responses;",
        string.Empty,
        $"/// <summary>The wire shape of {CSharpNames.Separated(ResponseName(spec)[..^"Response".Length], ' ')}.</summary>",
        $"public sealed record {ResponseName(spec)};",
    ];

    internal static List<Emission> Http(TextEdits.Editor editor, string root, UseCaseSpec spec)
    {
        if (!Directory.Exists(Path.Combine(root, ApiFolder(spec))))
        {
            return [];
        }

        string controller = $"{ApiFolder(spec)}/Features/{spec.Features}/Controllers/{spec.Features}Controller.cs";

        editor.Step = $"the action in {controller}";

        if (spec.Routing?.Failure is { } failure)
        {
            editor.Fail(failure);
        }
        else if (editor.Open(controller) is { } sheet)
        {
            AddAction(editor, sheet, spec);
        }

        editor.Step = "the route's row in docs/reference/api.md";
        AddRouteRow(editor, spec);

        return
        [
            .. spec.HasRequest ? [new Emission(RequestPath(spec), string.Join('\n', RequestContract(spec)) + "\n")] : Array.Empty<Emission>(),
            .. spec.IsCommand ? Array.Empty<Emission>() : [new Emission(ResponsePath(spec), string.Join('\n', ResponseContract(spec)) + "\n")],
        ];
    }

    internal static void AddAction(TextEdits.Editor editor, TextEdits.Sheet sheet, UseCaseSpec spec)
    {
        string declaration = $" {Remainder(spec)}(";

        if (TextEdits.Editor.Find(sheet, line => line.Contains("Task<ActionResult", StringComparison.Ordinal) && line.Contains(declaration, StringComparison.Ordinal)) >= 0)
        {
            editor.Kept.Add(editor.Step);

            return;
        }

        int type = TextEdits.Editor.Find(sheet, line => line.Contains(" class ", StringComparison.Ordinal) && line.Contains("Controller(", StringComparison.Ordinal));
        int parameters = type < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.Contains(") :", StringComparison.Ordinal), type);
        int closing = TextEdits.Editor.FindLast(sheet, line => line == "}", sheet.Lines.Count - 1);

        if (parameters < 0 || closing < parameters)
        {
            editor.Fail("it declares no controller with a primary constructor, and a closing brace at the start of a line, to take the action.");

            return;
        }

        if (TextEdits.Editor.Find(sheet, line => line.Contains("ISender ", StringComparison.Ordinal), type, parameters + 1) < 0)
        {
            TextEdits.Editor.Insert(sheet, type, before: false, ["    ISender sender,"]);
            closing++;
        }

        TextEdits.Editor.Insert(sheet, closing, before: true, ["", .. ActionLines(spec).Select(line => line.Length == 0 ? line : "    " + line)]);

        foreach (string space in ControllerUsings(spec))
        {
            editor.Using(sheet.Path, space);
        }
    }

    internal static void AddRouteRow(TextEdits.Editor editor, UseCaseSpec spec)
    {
        const string page = "docs/reference/api.md";

        if (editor.Open(page) is not { } sheet)
        {
            return;
        }

        string row = RouteRowText(spec);
        int routes = TextEdits.Editor.Find(sheet, line => line == "## Routes");
        int end = routes < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.StartsWith("## ", StringComparison.Ordinal), routes + 1);
        int section = routes < 0 ? -1 : TextEdits.Editor.Find(
            sheet,
            line => line.StartsWith("### ", StringComparison.Ordinal) && Letters(line[4..]) == Letters(spec.Features),
            routes + 1,
            end);
        int next = section < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.StartsWith('#'), section + 1);
        int rule = section < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.StartsWith("|---", StringComparison.Ordinal), section + 1, next);

        if (rule < 0)
        {
            editor.Fail($"{page} has no route table for {spec.Features} under '## Routes' to take the use case's row.");

            return;
        }

        int last = rule;

        while (last + 1 < sheet.Lines.Count && sheet.Lines[last + 1].StartsWith('|'))
        {
            last++;
        }

        if (TextEdits.Editor.Find(sheet, line => line == row, rule, last + 1) >= 0)
        {
            editor.Kept.Add(editor.Step);

            return;
        }

        TextEdits.Editor.Insert(sheet, last, before: false, [row]);
    }

    internal static string Letters(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    internal static string Construction(UseCaseSpec spec) =>
        $"new {spec.Input}({string.Join(", ", spec.Parameters.Select(parameter => parameter.Argument))})";

    internal static IEnumerable<string> Hosts(string root)
    {
        string presentation = Full(root, Presentation);

        return Directory.Exists(presentation)
            ? Directory.GetDirectories(presentation)
                .Where(folder => File.Exists(Path.Combine(folder, "Program.cs")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
            : [];
    }

    internal static string Remainder(UseCaseSpec spec) =>
        spec.Aggregate is { Length: > 0 } aggregate && spec.Name.Contains(aggregate, StringComparison.Ordinal) && spec.Name.Length > aggregate.Length
            ? spec.Name.Replace(aggregate, string.Empty, StringComparison.Ordinal)
            : spec.Name;

    internal static string IdName(UseCaseSpec spec) => spec.Aggregate is null ? string.Empty : CSharpNames.Lower(spec.Aggregate) + "Id";

    internal static string RouteTemplate(UseCaseSpec spec)
    {
        string remainder = Remainder(spec);
        string segment = CSharpNames.Kebab(!spec.IsCommand && remainder.StartsWith("Get", StringComparison.Ordinal) && remainder.Length > 3 ? remainder[3..] : remainder);

        return spec.HasId ? $"{spec.Routing?.Resource ?? IdSegment(spec)}/{segment}" : segment;
    }

    internal static string IdSegment(UseCaseSpec spec) => $"{{{IdName(spec)}:guid}}";

    internal static Routing? ReadRouting(string root, UseCaseSpec spec)
    {
        string controller = Path.Combine(root, ApiFolder(spec), "Features", spec.Features, "Controllers", spec.Features + "Controller.cs");

        if (!File.Exists(controller))
        {
            return null;
        }

        string text = File.ReadAllText(controller);
        string route = _controllerRoute.Match(text) is { Success: true } declared ? declared.Groups["template"].Value : string.Empty;
        bool atTheRoot = _rootRoute.IsMatch(route);
        string documented = "/" + (atTheRoot ? _versionSegment.Replace(route, "v1").Trim('/') + "/" : string.Empty);

        if (!spec.HasId)
        {
            return atTheRoot
                ? new Routing(string.Empty, documented, "the controller is routed at the API root, where each action carries its resource path, and a use case without the aggregate's id gives no resource to take it from. Write the action by hand with its full route.")
                : null;
        }

        string id = IdSegment(spec);
        string? carrying = _actionRoute.Matches(text)
            .Select(action => action.Groups["template"].Value)
            .Where(template => template.Contains(id, StringComparison.Ordinal))
            .OrderBy(template => template.Length)
            .FirstOrDefault();

        if (carrying is null)
        {
            return atTheRoot
                ? new Routing(id, documented, $"the controller is routed at the API root, where each action carries its resource path, and none of its actions carries {id} to take the path from. Write the action by hand with its full route.")
                : null;
        }

        return new Routing(carrying[..(carrying.IndexOf(id, StringComparison.Ordinal) + id.Length)], documented, null);
    }

    internal static string RouteRowText(UseCaseSpec spec)
    {
        string documented = (spec.Routing?.Documented ?? "/") + RouteTemplate(spec).Replace(":guid", string.Empty, StringComparison.Ordinal);

        return $"| {(spec.IsCommand ? "POST" : "GET")} | `{documented}` | {(spec.IsCommand ? "204" : "200")} | |";
    }

    internal static List<string> ActionLines(UseCaseSpec spec)
    {
        string id = IdName(spec);
        string response = spec.IsCommand ? string.Empty : ResponseName(spec);
        string[] signature = Signature(spec, id);
        string message = $"new {spec.Input}({string.Join(", ", Arguments(spec, id))})";
        string declaration = $"public async Task<ActionResult{(spec.IsCommand ? string.Empty : $"<{response}>")}> {Remainder(spec)}";
        var lines = Attributes(spec, response);

        if (!spec.HasRequest && !(spec.IsCommand && spec.HasId))
        {
            lines.AddRange([$"{declaration}({string.Join(", ", signature)}) =>", $"    {Answer(spec, response, message)};"]);

            return lines;
        }

        lines.Add(spec.HasRequest
            ? $"{declaration}(\n{string.Join(",\n", signature.Select(parameter => "    " + parameter))})"
            : $"{declaration}({string.Join(", ", signature)})");
        lines.Add("{");
        lines.AddRange(Body(spec, message, response));
        lines.Add("}");

        return [.. lines.SelectMany(line => line.Split('\n'))];
    }

    private static string[] Signature(UseCaseSpec spec, string id)
    {
        string binding = spec.IsCommand ? "FromBody" : "FromQuery";

        return
        [
            .. spec.HasId ? new[] { $"Guid {id}" } : [],
            .. spec.HasRequest ? new[] { $"[{binding}] {spec.Request} request" } : [],
            "CancellationToken cancellationToken",
        ];
    }

    private static string[] Arguments(UseCaseSpec spec, string id) =>
    [
        .. spec.HasId ? new[] { id } : [],
        .. spec.Inputs.Select(parameter => "request." + parameter.Name),
        .. spec.IsCommand && spec.HasId ? new[] { "precondition" } : [],
    ];

    private static List<string> Attributes(UseCaseSpec spec, string response)
    {
        var lines = new List<string>
        {
            $"/// <summary>{UseCasePlan.Sentence(spec.Name)}.</summary>",
            $"[Http{(spec.IsCommand ? "Post" : "Get")}(\"{RouteTemplate(spec)}\")]",
            spec.IsCommand
                ? "[ProducesResponseType(StatusCodes.Status204NoContent)]"
                : $"[ProducesResponseType(StatusCodes.Status200OK, Type = typeof({response}))]",
        };

        if (spec.HasId || spec.HasRequest)
        {
            lines.Add("[ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]");
        }

        if (spec.HasId)
        {
            lines.Add("[ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]");
        }

        if (spec.IsCommand && spec.HasId)
        {
            lines.AddRange(
            [
                "[ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]",
                "[ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]",
                "[ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]",
            ]);
        }

        return lines;
    }

    private static string Answer(UseCaseSpec spec, string response, string sent) => spec.IsCommand
        ? $"NoContentOrProblem(await sender.Send({sent}, cancellationToken))"
        : $"OkOrProblem((await sender.Send({sent}, cancellationToken)).Map(_ => new {response}()))";

    private static List<string> Body(UseCaseSpec spec, string message, string response)
    {
        var lines = new List<string>();

        if (spec.HasRequest)
        {
            lines.AddRange(["    ArgumentNullException.ThrowIfNull(request);", string.Empty]);
        }

        if (spec.IsCommand && spec.HasId)
        {
            lines.AddRange(
            [
                "    if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)",
                "    {",
                "        return refusal;",
                "    }",
                string.Empty,
                $"    var outcome = await sender.Send({message}, cancellationToken);",
                string.Empty,
                "    return NoContentOrProblem(RequiringExistence(requiresExistence, outcome));",
            ]);
        }
        else
        {
            string local = spec.IsCommand ? "command" : "query";

            lines.AddRange([$"    var {local} = {message};", string.Empty, $"    return {Answer(spec, response, local)};"]);
        }

        return lines;
    }
}
