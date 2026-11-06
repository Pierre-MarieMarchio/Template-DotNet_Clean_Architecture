using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.AddUseCase;
using CleanArch.Cli.Generators.Shared;
using Xunit;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Tests.Generators;

public sealed class NewUseCaseTests
{
    [Fact]
    public void Parser() => Expectations.Hold(ParserCases);

    [Fact]
    public void Input() => Expectations.Hold(InputCases);

    [Fact]
    public void Tree() => Expectations.Hold(TreeCases);

    [Fact]
    public void Applied() => Expectations.Hold(AppliedCases);

    [Fact]
    public void AllOrNothing() => Expectations.Hold(AllOrNothingCases);

    [Fact]
    public void Idempotence() => Expectations.Hold(IdempotenceCases);

    [Fact]
    public void Shape() => Expectations.Hold(ShapeCases);

    [Fact]
    public void Content() => Expectations.Hold(ContentCases);

    [Fact]
    public void Dto() => Expectations.Hold(DtoCases);

    [Fact]
    public void Manifest() => Expectations.Hold(ManifestCases);

    [Fact]
    public void Rollback() => Expectations.Hold(RollbackCases);

    [Fact]
    public void WithoutAnApi() => Expectations.Hold(WithoutTheApiCases);

    [Fact]
    public void Routing() => Expectations.Hold(RoutingCases);

    private static string NewTree(string scratch, params string[] repositories)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        string[] directories =
        [
            "Src/Domain/A.Domain/Features/Invoices/Repositories",
            "Src/Application/A.Application/Features/Invoices/UseCases/Commands/CreateInvoice",
            "Src/Application/A.Application/Features/Notes/Services",
            "Src/Presentation/A.Api/Features/Invoices/Controllers",
            "Tests/Application/A.Application.UnitTests/TestDoubles",
        ];

        foreach (string directory in directories)
        {
            Directory.CreateDirectory(Path.Combine(root, directory));
        }

        File.WriteAllText(Path.Combine(root, "cleanarch.json"), "{\n  \"name\": \"Acme\"\n}\n");
        File.WriteAllText(Path.Combine(root, "Tests/Application/A.Application.UnitTests/TestDoubles/ValidatedHandler.cs"), "the test double a person keeps\n");

        foreach (string repository in repositories.Length == 0 ? ["Invoice"] : repositories)
        {
            File.WriteAllText(
                Path.Combine(root, $"Src/Domain/A.Domain/Features/Invoices/Repositories/I{repository}Repository.cs"),
                $"public interface I{repository}Repository;\n");
        }

        File.WriteAllText(Path.Combine(root, _controller), _controllerText);
        Directory.CreateDirectory(Path.Combine(root, "docs/reference"));
        File.WriteAllText(Path.Combine(root, _apiPage), _apiPageText);

        return root;
    }

    private const string _controller = "Src/Presentation/A.Api/Features/Invoices/Controllers/InvoicesController.cs";

    private static readonly string[] _servingFiles =
    [
        _controller,
        _apiPage,
    ];

    private const string _controllerText =
        """
        using Microsoft.AspNetCore.Mvc;

        namespace Acme.Api.Features.Invoices.Controllers;

        [ApiController]
        public sealed class InvoicesController(
            IInvoiceService service) : ControllerBase
        {
            [HttpGet]
            public Task<ActionResult> List() => service.ListAsync();
        }

        """;

    private const string _apiPage = "docs/reference/api.md";

    private const string _apiPageText =
        """
        # API

        ## Routes

        ### Invoices

        Under `/api/v1/invoices`.

        | Method | Route | Success | Notes |
        |---|---|---|---|
        | GET | `/` | 200 | |

        ## Error codes

        """;

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args) => Invoke(root, _ => 0, args);

    private static (int Code, string Output, string Error) Invoke(string root, Func<TextEdits.Command, int> run, params string[] args) =>
        GeneratorRun.Invoke(AddUseCaseCommand.Build(GeneratorRun.Project(root), _ => new FakeExecutor(run)), args);

    private static Func<TextEdits.Command, int> Recording(List<TextEdits.Command> ran, string? failing = null) =>
        command =>
        {
            ran.Add(command);

            return command.Display == failing ? 1 : 0;
        };

    private static int Occurrences(string root, string path, string line) =>
        File.Exists(Path.Combine(root, path)) ? File.ReadAllLines(Path.Combine(root, path)).Count(candidate => candidate.Trim() == line) : -1;

    private static void AppliedCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var ran = new List<TextEdits.Command>();
        var result = Invoke(root, Recording(ran), "Invoices", "FindByCustomer", "--kind", "query", "--input", "id, Customer:string");

        expect(result.Code == 0, $"The applied use case was refused: {result.Error}");

        (string Path, string Line, int Count)[] expected =
        [
            (_controller, "ISender sender,", 1),
            (_controller, "IInvoiceService service) : ControllerBase", 1),
            (_controller, "[HttpGet(\"{invoiceId:guid}/find-by-customer\")]", 1),
            (_controller, "[FromQuery] FindByCustomerRequest request,", 1),
            (_controller, "using Acme.Application.Features.Invoices.UseCases.Queries.FindByCustomer;", 1),
            (_controller, "using Acme.Api.Features.Invoices.Contracts.Requests;", 1),
            (_controller, "using Acme.Api.Features.Invoices.Contracts.Responses;", 1),
            (_controller, "using Mediator;", 1),
            (_apiPage, "| GET | `/{invoiceId}/find-by-customer` | 200 | |", 1),
            ("Src/Presentation/A.Api/Features/Invoices/Contracts/Requests/FindByCustomerRequest.cs", "public sealed record FindByCustomerRequest(string Customer);", 1),
            ("Src/Presentation/A.Api/Features/Invoices/Contracts/Responses/FindByCustomerResponse.cs", "public sealed record FindByCustomerResponse;", 1),
        ];

        foreach (var (path, line, count) in expected)
        {
            expect(Occurrences(root, path, line) == count, $"The applied use case did not leave '{line}' {count} time(s) in {path}.");
        }

        string[] controller = File.ReadAllLines(Path.Combine(root, _controller));

        expect(controller[^1] == "}" && controller[^2] == "    }" && controller.Count(line => line == "}") == 1, "The action was not placed inside the controller, before its closing brace.");

        string[] usings = [.. controller.Where(line => line.StartsWith("using ", StringComparison.Ordinal))];

        expect(usings.SequenceEqual(usings.Order(StringComparer.OrdinalIgnoreCase)), $"The controller's usings are out of order: {string.Join(" ", usings)}");
        expect(
            ran.Select(command => command.Display).SequenceEqual(["dotnet run Tools/Tasks.cs openapi"]),
            $"The applied use case did not rewrite the OpenAPI reference alone: {string.Join(" | ", ran.Select(command => command.Display))}");

        var again = Invoke(root, "Invoices", "FindByCustomer", "--kind", "query", "--input", "id, Customer:string");

        expect(again.Code == 1 && again.Error.Contains("already has a use case called FindByCustomer", StringComparison.Ordinal), "Running the same use case again was not refused.");
        expect(Occurrences(root, _controller, "ISender sender,") == 1, "A second action asked for a second ISender.");
    }

    private static void AllOrNothingCases(string scratch, Action<bool, string> expect)
    {
        (string Label, Action<string> Break, string Fragment)[] breaks =
        [
            ("no route table", root => File.WriteAllText(Path.Combine(root, _apiPage), "# API\n\n## Routes\n\n## Error codes\n"), "has no route table for Invoices"),
            ("no API page", root => File.Delete(Path.Combine(root, _apiPage)), "docs/reference/api.md does not exist"),
            ("no closing brace", root => File.WriteAllText(Path.Combine(root, _controller), _controllerText.Replace("\n}\n", "\n", StringComparison.Ordinal)), "closing brace"),
            ("no controller", root => File.Delete(Path.Combine(root, _controller)), "InvoicesController.cs does not exist"),
        ];

        foreach (var (label, breaking, fragment) in breaks)
        {
            string root = NewTree(scratch);

            breaking(root);

            var before = Snapshot(root);
            var ran = new List<TextEdits.Command>();
            var result = Invoke(root, Recording(ran), "Invoices", "GetInvoiceTotals", "--kind", "query");

            expect(result.Code == 1 && Same(before, Snapshot(root)) && ran.Count == 0, $"[{label}] a missing anchor did not stop the use case before it wrote or ran.");
            expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] the refusal does not say '{fragment}': {result.Error}");
            expect(result.Output.Contains("the edit by hand in", StringComparison.Ordinal), $"[{label}] the refusal does not print the edit to make by hand.");
        }
    }

    private static void IdempotenceCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string withAction = _controllerText.Replace(
            "    public Task<ActionResult> List()",
            "    public async Task<ActionResult> Approve(Guid invoiceId, CancellationToken cancellationToken) => NoContent();\n\n    public Task<ActionResult> List()",
            StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(root, _controller), withAction);
        File.WriteAllText(Path.Combine(root, _apiPage), _apiPageText.Replace("| GET | `/` | 200 | |", "| GET | `/` | 200 | |\n| POST | `/{invoiceId}/approve` | 204 | |", StringComparison.Ordinal));

        var result = Invoke(root, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(result.Code == 0, $"A use case whose action a person had written was refused: {result.Error}");
        expect(File.ReadAllText(Path.Combine(root, _controller)) == withAction, "An action already in place was written a second time.");
        expect(Occurrences(root, _apiPage, "| POST | `/{invoiceId}/approve` | 204 | |") == 1, "A route row already in place was written a second time.");

        string dry = NewTree(scratch);
        var before = Snapshot(dry);
        var ran = new List<TextEdits.Command>();
        var preview = Invoke(dry, Recording(ran), "Invoices", "ApproveInvoice", "--kind", "command", "--dry-run");

        expect(preview.Code == 0 && Same(before, Snapshot(dry)) && ran.Count == 0, "A dry run wrote or ran something.");
        expect(
            preview.Output.Contains("makes in Src/Presentation/A.Api/Features/Invoices/Controllers/InvoicesController.cs", StringComparison.Ordinal)
                && preview.Output.Contains("nothing was written and nothing was run", StringComparison.Ordinal),
            "A dry run did not print the edit it would make.");

        string failing = NewTree(scratch);
        var failed = Invoke(failing, Recording([], failing: "dotnet run Tools/Tasks.cs openapi"), "Invoices", "ApproveInvoice", "--kind", "command");

        expect(
            failed.Code == 1 && failed.Error.Contains("  dotnet run Tools/Tasks.cs openapi", StringComparison.Ordinal) && Occurrences(failing, _controller, "ISender sender,") == 1,
            "A failed OpenAPI step did not fail the command and name what remains, or undid the edit.");
    }

    private static string RoutedController(string route, params string[] actions) =>
        _controllerText.Replace(
            "[ApiController]\n",
            $"[ApiController]\n[Route(\"{route}\")]\n",
            StringComparison.Ordinal)
            .Replace(
                "    [HttpGet]\n    public Task<ActionResult> List() => service.ListAsync();",
                string.Join("\n\n", actions.Select(action => $"    {action}\n    public Task<ActionResult> Action{Array.IndexOf(actions, action)}() => service.ListAsync();")),
                StringComparison.Ordinal);

    private static void RoutingCases(string scratch, Action<bool, string> expect)
    {
        (string Label, string Route, string[] Actions, string Action, string? Row)[] served =
        [
            ("the resource in the controller's route", "api/v{version:apiVersion}/invoices", ["[HttpGet(\"{invoiceId:guid}\")]"], "[HttpPost(\"{invoiceId:guid}/approve\")]", "| POST | `/{invoiceId}/approve` | 204 | |"),
            ("the resource in an action of a controller routed at the root", "api/v{version:apiVersion}", ["[HttpGet(\"invoices\")]", "[HttpGet(\"billing/{invoiceId:guid}/lines/{lineId:guid}\")]", "[HttpPut(\"invoices/{invoiceId:guid}\")]"], "[HttpPost(\"invoices/{invoiceId:guid}/approve\")]", "| POST | `/api/v1/invoices/{invoiceId}/approve` | 204 | |"),
        ];

        foreach (var (label, route, actions, action, row) in served)
        {
            string root = NewTree(scratch);

            File.WriteAllText(Path.Combine(root, _controller), RoutedController(route, actions));

            var result = Invoke(root, "Invoices", "ApproveInvoice", "--kind", "command");

            expect(result.Code == 0, $"[{label}] the use case was refused: {result.Error}");
            expect(Occurrences(root, _controller, action) == 1, $"[{label}] the action did not carry {action}.");
            string written = row!.StartsWith("| POST | `/api/", StringComparison.Ordinal) ? "written in full, from /api" : "relative to that table's 'Under' prefix";

            expect(result.Output.Contains(written, StringComparison.Ordinal), $"[{label}] the output does not say how the row is written.");
            expect(Occurrences(root, _apiPage, row) == 1, $"[{label}] the route's row was not {row}.");
        }

        (string Label, string[] Arguments, string Fragment)[] refused =
        [
            ("an action carries no id", ["Invoices", "ApproveInvoice", "--kind", "command"], "none of its actions carries {invoiceId:guid}"),
            ("a message without the id", ["Invoices", "CloseBooks", "--kind", "command", "--input", "none"], "a use case without the aggregate's id"),
        ];

        foreach (var (label, arguments, fragment) in refused)
        {
            string root = NewTree(scratch);

            File.WriteAllText(Path.Combine(root, _controller), RoutedController("api/v{version:apiVersion}", "[HttpGet(\"invoices\")]"));

            var before = Snapshot(root);
            var result = Invoke(root, arguments);

            expect(result.Code == 1 && Same(before, Snapshot(root)), $"[{label}] a root-routed controller without a resource path to read did not stop the use case before it wrote.");
            expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] the refusal does not say '{fragment}': {result.Error}");
        }
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "<directory>",
                StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    private static void ExpectRefusal(string root, string[] args, string fragment, Action<bool, string> expect)
    {
        var before = Snapshot(root);
        var result = Invoke(root, args);
        string label = string.Join(' ', args);

        expect(result.Code == 1, $"[{label}] exited {result.Code}, not 1.");
        expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] did not say '{fragment}': {result.Error.Split('\n')[0]}");
        expect(result.Error.Contains("Nothing was written.", StringComparison.Ordinal), $"[{label}] did not say nothing was written.");
        expect(Same(before, Snapshot(root)), $"[{label}] changed the tree.");
    }

    private static void ParserCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        (string[] Args, string Fragment)[] refusals =
        [
            (["Invoices", "ApproveInvoice", "--kind", "event"], "--kind 'event' is not a choice"),
            (["Invoices", "ApproveInvoice", "--kind", "command", "--input", "body"], "'body' in --input is neither id nor an input"),
            (["invoices", "ApproveInvoice", "--kind", "command"], "is not a feature folder"),
            (["Invoices", "approveInvoice", "--kind", "command"], "is not a use case name"),
            (["Invoices", "Invoices", "--kind", "command"], "are both 'Invoices'"),
            (["Invoices", "Queries", "--kind", "query"], "already a segment"),
            (["Invoices", "ApproveInvoiceUseCase", "--kind", "command"], "ends in 'UseCase'"),
            (["Invoices", "ApproveInvoiceCommand", "--kind", "command"], "ends in 'Command'"),
            (["Invoices", "Event", "--kind", "command"], "C# keyword"),
            (["Invoices", new string('A', 61), "--kind", "command"], "longer than 60"),
        ];

        foreach (var (args, fragment) in refusals)
        {
            ExpectRefusal(root, args, fragment, expect);
        }

        var several = Invoke(root, "invoices", "approve", "--kind", "event", "--input", "body");

        expect(
            several.Error.Split('\n').Count(line => line.StartsWith("error:", StringComparison.Ordinal)) >= 4,
            "Several mistakes in one command were not all reported.");
    }

    private static void InputCases(string scratch, Action<bool, string> expect)
    {
        (string Input, bool HasId, string[] Declared)[] accepted =
        [
            ("none", false, []),
            ("id", true, []),
            ("Name:string, MaxCount:int", false, ["string Name", "int MaxCount"]),
            ("id, Tags:IReadOnlyList<string>", true, ["IReadOnlyList<string> Tags"]),
            ("Lookup:Dictionary<string,int>", false, ["Dictionary<string, int> Lookup"]),
            (" Map : Dictionary< string , List<int?> > , Ids:Guid[]", false, ["Dictionary<string, List<int?>> Map", "Guid[] Ids"]),
            ("Grid:int[,], Note:string?, Due:DateOnly?, Matrix:int?[][]", false, ["int[,] Grid", "string? Note", "DateOnly? Due", "int?[][] Matrix"]),
            ("Pairs:List<(string Key, int Value)>", false, ["List<(string Key, int Value)> Pairs"]),
            ("Owner:global::System.Guid, Nested:Outer<int>.Inner", false, ["global::System.Guid Owner", "Outer<int>.Inner Nested"]),
        ];

        foreach (var (input, hasId, declared) in accepted)
        {
            var problems = new List<string>();
            var (readId, inputs) = UseCaseInputs.ReadInputs(input, problems);
            string[] read = [.. inputs.Select(parameter => $"{parameter.Type} {parameter.Name}")];

            expect(problems.Count == 0, $"[--input {input}] was refused: {string.Join(" ", problems)}");
            expect(readId == hasId, $"[--input {input}] read the aggregate's id as {(readId ? "present" : "absent")}.");
            expect(read.SequenceEqual(declared), $"[--input {input}] read {string.Join("; ", read)}, not {string.Join("; ", declared)}.");
        }

        (string Input, string Fragment)[] refused =
        [
            ("Name", "neither id nor an input written Name:Type"),
            ("name:string", "'name' in --input is not an input name"),
            (":string", "'' in --input is not an input name"),
            ("Name:", "it stops short of a complete type"),
            ("Name:List<string", "it stops where '>' is due"),
            ("Name:List<>", "'>' stands where a name is due"),
            ("Name:List<string>>", "'>' follows a type that was already complete"),
            ("Name:int x", "'x' follows a type that was already complete"),
            ("Name:int??", "a type is made nullable once"),
            ("Name:class", "'class' is a C# keyword, not a type"),
            ("Name:void", "'void' is a C# keyword, not a type"),
            ("Name:System.string", "'string' is a C# keyword, not a type"),
            ("Name:1abc", "'1' has no place in a type name"),
            ("Name:(int)", "a tuple holds two elements or more"),
            ("Name:(int, string in)", "'in' is a C# keyword, not an element name"),
            ("Name:string, Name:int", "--input names Name more than once"),
            ("Tags:string, id", "id comes first in --input"),
            ("id, id", "id comes first in --input"),
            ("none, Name:string", "none stands alone in --input"),
            ("Name:string,", "holds an empty entry"),
            ("ToString:string", "a member every record declares already"),
        ];

        string root = NewTree(scratch);

        foreach (var (input, fragment) in refused)
        {
            ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", input], fragment, expect);
        }

        ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", "id, InvoiceId:Guid"], "The input InvoiceId would clash", expect);
        ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", "id, Precondition:string"], "The input Precondition would clash", expect);
        ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", "ApproveInvoiceCommand:int"], "would clash", expect);
        expect(
            Invoke(root, "Invoices", "ReviewInvoice", "--kind", "query", "--input", "id, Precondition:string").Code == 0,
            "A query was refused an input named Precondition, a name only a command with an id holds.");
    }

    private static void ContentCases(string scratch, Action<bool, string> expect)
    {
        string useCases = "Src/Application/A.Application/Features/Invoices/UseCases";
        string tests = "Tests/Application/A.Application.UnitTests/Features/Invoices/UseCases";
        string root = NewTree(scratch);

        string Read(string relative) => File.Exists(Path.Combine(root, relative)) ? File.ReadAllText(Path.Combine(root, relative)) : string.Empty;

        void Holds(string text, string fragment, string what) =>
            expect(text.ReplaceLineEndings("\n").Contains(fragment, StringComparison.Ordinal), $"{what} does not hold '{fragment}'.");

        var query = Invoke(root, "Invoices", "FindInvoicesByCustomer", "--kind", "query", "--input", "Customer:string, Limit:int, Since:DateOnly?, Until:DateTimeOffset?");
        string queryFolder = $"{useCases}/Queries/FindInvoicesByCustomer";
        string queryTests = $"{tests}/Queries/FindInvoicesByCustomer";

        expect(query.Code == 0, $"The parameterised query was refused: {query.Error}");
        Holds(Read($"{queryFolder}/FindInvoicesByCustomer.cs"), "public sealed record FindInvoicesByCustomerQuery(\n    string Customer,\n    int Limit,\n    DateOnly? Since,\n    DateTimeOffset? Until) : IQuery<Result<FindInvoicesByCustomerDto>>;", "The query");
        Holds(Read($"{queryFolder}/FindInvoicesByCustomerQueryValidator.cs"), "RuleFor(query => query.Customer)\n            .NotEmpty().WithMessage(\"Customer is required.\");", "The query's validator");
        Holds(Read($"{queryFolder}/FindInvoicesByCustomerQueryValidator.cs"), "RuleFor(query => query.Limit)", "The query's validator");
        expect(!Read($"{queryFolder}/FindInvoicesByCustomerQueryValidator.cs").Contains("Since", StringComparison.Ordinal), "A nullable input was given a rule.");
        expect(!Read("Src/Application/A.Application/Features/Invoices/Model/Dtos/FindInvoicesByCustomerDto.cs").Contains("Id", StringComparison.Ordinal), "A query without the id answers a DTO carrying it.");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerQueryValidatorTests.cs"), "new(\n            \"customer\",\n            1,\n            new DateOnly(2026, 1, 1),\n            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));", "The query's validator tests");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerQueryValidatorTests.cs"), "AWellFormedQuery() with { Customer = string.Empty }", "The query's validator tests");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerQueryValidatorTests.cs"), "AWellFormedQuery() with { Limit = 0 }", "The query's validator tests");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerTests.cs"), "new(new FindInvoicesByCustomerQueryHandler().Handle, new FindInvoicesByCustomerQueryValidator());", "The query's handler test");
        expect(
            Regex.Count(Read($"{queryTests}/FindInvoicesByCustomerTests.cs"), "QueryHandler\\(", RegexOptions.None, TimeSpan.FromSeconds(1)) == 1,
            "The query's handler test builds its handler in more than one place.");
        Holds(query.Output, "      [FromQuery] FindInvoicesByCustomerRequest request,", "The query's action");
        Holds(query.Output, "          var query = new FindInvoicesByCustomerQuery(request.Customer, request.Limit, request.Since, request.Until);", "The query's action");
        Holds(query.Output, "          return OkOrProblem((await sender.Send(query, cancellationToken)).Map(_ => new FindInvoicesByCustomerResponse()));", "The query's action");
        Holds(query.Output, "                       Acme.Api.Features.Invoices.Contracts.Requests;", "The query's usings");
        Holds(query.Output, "  and Src/Presentation/A.Api/Features/Invoices/Contracts/Requests/FindInvoicesByCustomerRequest.cs, with what a client sends:", "The query's request contract");
        Holds(query.Output, "      public sealed record FindInvoicesByCustomerRequest(\n          string Customer,", "The query's request contract");
        expect(!query.Output.Contains("Written as given", StringComparison.Ordinal), "Inputs of known types were listed as unknown.");

        var command = Invoke(root, "Invoices", "RetagInvoice", "--kind", "command", "--input", "id, Tags:IReadOnlyList<string>");
        string commandFolder = $"{useCases}/Commands/RetagInvoice";

        expect(command.Code == 0, $"The command with the id and a body was refused: {command.Error}");
        Holds(Read($"{commandFolder}/RetagInvoice.cs"), "    Guid InvoiceId,\n    IReadOnlyList<string> Tags,\n    VersionPrecondition? Precondition = null) : ICommand<Result>;", "The command");
        Holds(Read($"{tests}/Commands/RetagInvoice/RetagInvoiceCommandValidatorTests.cs"), "        new(Guid.CreateVersion7(), default!);", "The command's validator tests");
        Holds(Read($"{tests}/Commands/RetagInvoice/RetagInvoiceTests.cs"), "AWellFormedCommand() with { InvoiceId = Guid.Empty }", "The command's handler test");
        Holds(command.Output, "Written as given, of a type this command knows no rule and no test value for:\n  Tags  IReadOnlyList<string>", "The command's report");
        Holds(command.Output, "a real value in AWellFormedCommand() in RetagInvoiceCommandValidatorTests, which passes default! for it", "The command's report");
        Holds(command.Output, "      [FromBody] RetagInvoiceRequest request,", "The command's action");
        Holds(command.Output, "new RetagInvoiceCommand(invoiceId, request.Tags, precondition)", "The command's action");
        Holds(command.Output, "/// <summary>The body of retag invoice; the invoice id travels in the route.</summary>", "The command's request contract");

        var bare = Invoke(root, "Invoices", "FlagInvoices", "--kind", "command", "--input", "Labels:HashSet<string>, Urgent:bool");

        expect(bare.Code == 0, $"A command without a rule to write was refused: {bare.Error}");
        Holds(bare.Output, "No validator and no test were written", "The report of a command without a rule");
        Holds(Read($"{useCases}/Commands/FlagInvoices/FlagInvoices.cs"), "    HashSet<string> Labels,\n    bool Urgent) : ICommand<Result>;", "The command without a rule");

        var none = Invoke(root, "Invoices", "PingInvoices", "--kind", "command", "--input", "none");

        expect(none.Code == 0, $"A command without input was refused: {none.Error}");
        Holds(Read($"{useCases}/Commands/PingInvoices/PingInvoices.cs"), "public sealed record PingInvoicesCommand : ICommand<Result>;", "The command without input");
    }

    private static void TreeCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        ExpectRefusal(root, ["Orders", "ApproveOrder", "--kind", "command"], "No feature called Orders", expect);
        ExpectRefusal(root, ["Orders", "ApproveOrder", "--kind", "command"], "The features here are Invoices, Notes.", expect);
        ExpectRefusal(root, ["Notes", "ArchiveNote", "--kind", "command"], "it is a service over a store", expect);
        ExpectRefusal(root, ["Invoices", "CreateInvoice", "--kind", "command"], "already has a use case called CreateInvoice", expect);
        ExpectRefusal(root, ["Invoices", "CreateInvoice", "--kind", "query"], "already has a use case called CreateInvoice", expect);
        ExpectRefusal(root, ["Invoices", "Invoice", "--kind", "query"], "the aggregate's own name", expect);

        string none = NewTree(scratch, "Unused");
        File.Delete(Path.Combine(none, "Src/Domain/A.Domain/Features/Invoices/Repositories/IUnusedRepository.cs"));

        ExpectRefusal(none, ["Invoices", "ApproveInvoice", "--kind", "command"], "declares no repository", expect);
        expect(Invoke(none, "Invoices", "ApproveInvoice", "--kind", "command", "--input", "none").Code == 0, "--input none was refused where no repository names the aggregate.");

        string two = NewTree(scratch, "Invoice", "CreditNote");

        ExpectRefusal(two, ["Invoices", "ApproveInvoice", "--kind", "command"], "several (CreditNote, Invoice)", expect);

        string bare = NewTree(scratch);
        Directory.Delete(Path.Combine(bare, "Src/Application/A.Application/Features"), recursive: true);

        ExpectRefusal(bare, ["Invoices", "ApproveInvoice", "--kind", "command"], "This tree has no feature yet", expect);

        string blocked = NewTree(scratch);
        string inTheWay = Path.Combine(blocked, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceTotalsDto.cs");
        string test = Path.Combine(blocked, "Tests/Application/A.Application.UnitTests/Features/Invoices/UseCases/Commands/ApproveInvoice/ApproveInvoiceCommandValidatorTests.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(inTheWay)!);
        File.WriteAllText(inTheWay, "somebody's own dto\n");
        Directory.CreateDirectory(Path.GetDirectoryName(test)!);
        File.WriteAllText(test, "somebody's own test\n");

        ExpectRefusal(blocked, ["Invoices", "ApproveInvoice", "--kind", "command"], "none is ever overwritten", expect);
        expect(File.ReadAllText(test) == "somebody's own test\n", "An existing file was overwritten.");
    }

    private static void ShapeCases(string scratch, Action<bool, string> expect)
    {
        string useCases = "Src/Application/A.Application/Features/Invoices/UseCases";
        string tests = "Tests/Application/A.Application.UnitTests/Features/Invoices/UseCases";
        string dtos = "Src/Application/A.Application/Features/Invoices/Model/Dtos";

        (string[] Args, string[] Files)[] cases =
        [
            (["ApproveInvoice", "--kind", "command"],
                [$"{useCases}/Commands/ApproveInvoice/ApproveInvoice.cs", $"{useCases}/Commands/ApproveInvoice/ApproveInvoiceCommandValidator.cs",
                 $"{tests}/Commands/ApproveInvoice/ApproveInvoiceCommandValidatorTests.cs", $"{tests}/Commands/ApproveInvoice/ApproveInvoiceTests.cs"]),
            (["CloseOverdueInvoices", "--kind", "command", "--input", "none"],
                [$"{useCases}/Commands/CloseOverdueInvoices/CloseOverdueInvoices.cs"]),
            (["GetInvoiceTotals", "--kind", "query"],
                [$"{useCases}/Queries/GetInvoiceTotals/GetInvoiceTotals.cs", $"{useCases}/Queries/GetInvoiceTotals/GetInvoiceTotalsQueryValidator.cs",
                 $"{dtos}/InvoiceTotalsDto.cs", $"{tests}/Queries/GetInvoiceTotals/GetInvoiceTotalsQueryValidatorTests.cs",
                 $"{tests}/Queries/GetInvoiceTotals/GetInvoiceTotalsTests.cs"]),
            (["SummariseRevenue", "--kind", "query", "--input=none"],
                [$"{useCases}/Queries/SummariseRevenue/SummariseRevenue.cs", $"{dtos}/SummariseRevenueDto.cs"]),
            (["FindInvoicesByCustomer", "--kind", "query", "--input", "Customer:string, Limit:int, Since:DateOnly?"],
                [$"{useCases}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomer.cs", $"{useCases}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomerQueryValidator.cs",
                 $"{dtos}/FindInvoicesByCustomerDto.cs", $"{tests}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomerQueryValidatorTests.cs",
                 $"{tests}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomerTests.cs"]),
            (["RetagInvoice", "--kind", "command", "--input", "id, Tags:IReadOnlyList<string>"],
                [$"{useCases}/Commands/RetagInvoice/RetagInvoice.cs", $"{useCases}/Commands/RetagInvoice/RetagInvoiceCommandValidator.cs",
                 $"{tests}/Commands/RetagInvoice/RetagInvoiceCommandValidatorTests.cs", $"{tests}/Commands/RetagInvoice/RetagInvoiceTests.cs"]),
            (["FlagInvoices", "--kind", "command", "--input", "Labels:HashSet<string>, Urgent:bool"],
                [$"{useCases}/Commands/FlagInvoices/FlagInvoices.cs"]),
        ];

        foreach (var (args, files) in cases)
        {
            string root = NewTree(scratch);
            var before = Snapshot(root);
            var result = Invoke(root, ["Invoices", .. args]);
            string label = string.Join(' ', args);

            expect(result.Code == 0, $"[{label}] was refused: {result.Error}");

            var after = Snapshot(root);
            string[] created = [.. after.Keys.Except(before.Keys, StringComparer.Ordinal).Where(key => after[key] != "<directory>").Order(StringComparer.Ordinal)];

            expect(
                created.Where(path => !path.Contains("/Contracts/", StringComparison.Ordinal)).SequenceEqual(files.Order(StringComparer.Ordinal)),
                $"[{label}] wrote {string.Join(", ", created)}.");
            expect(
                before.Where(pair => !_servingFiles.Contains(pair.Key)).All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
                $"[{label}] changed a file it neither owns nor serves the use case through.");
            expect(!Directory.Exists(Path.Combine(root, FeatureManifest.Folder)), $"[{label}] wrote a manifest where the feature had none.");
            expect(result.Output.Contains("dotnet run Tools/Tasks.cs openapi", StringComparison.Ordinal), $"[{label}] did not print the OpenAPI step.");
            expect(
                !result.Output.Contains("api-client", StringComparison.Ordinal) && !result.Output.Contains("pnpm api", StringComparison.Ordinal),
                $"[{label}] printed a step for a client and a web workspace the project does not hold.");
            expect(result.Output.Contains("Src/Presentation/A.Api/Features/Invoices/Controllers/InvoicesController.cs", StringComparison.Ordinal), $"[{label}] did not name the feature's controller.");

            string row = args[0] switch
            {
                "ApproveInvoice" => "| POST | `/{invoiceId}/approve` | 204 | |",
                "CloseOverdueInvoices" => "| POST | `/close-overdue-invoices` | 204 | |",
                "GetInvoiceTotals" => "| GET | `/{invoiceId}/totals` | 200 | |",
                "FindInvoicesByCustomer" => "| GET | `/find-invoices-by-customer` | 200 | |",
                "RetagInvoice" => "| POST | `/{invoiceId}/retag` | 204 | |",
                "FlagInvoices" => "| POST | `/flag-invoices` | 204 | |",
                _ => "| GET | `/summarise-revenue` | 200 | |",
            };

            expect(result.Output.Contains("ApiRouteTableTests", StringComparison.Ordinal), $"[{label}] did not name the test that reads the route table.");
            expect(result.Output.Contains($"  {row}", StringComparison.Ordinal), $"[{label}] did not print the route's row '{row}'.");

            foreach (string file in created)
            {
                foreach (string problem in FileProblems(root, file))
                {
                    expect(false, $"[{label}] {problem}");
                }
            }
        }

        string contractRoot = NewTree(scratch);

        Directory.CreateDirectory(Path.Combine(contractRoot, "Src/Presentation/A.Presentation.Client"));
        Directory.CreateDirectory(Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts"));
        File.WriteAllText(Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts/generate.mjs"), "// the script pnpm api runs\n");

        var ran = new List<TextEdits.Command>();
        var contract = Invoke(contractRoot, Recording(ran), "Invoices", "ApproveInvoice", "--kind", "command");

        string[] launched = [.. ran.Select(command => string.Join(' ', [Path.GetFileNameWithoutExtension(command.FileName), .. command.Arguments]))];

        expect(
            contract.Code == 0
                && launched.SequenceEqual(["dotnet run Tools/Tasks.cs openapi", "dotnet run Tools/Tasks.cs api-client", "pnpm api --no-build"]),
            $"The OpenAPI reference, which builds the API, then the client and the web schema from that build were not all run, in that order, for a project that holds the client and the web workspace: {string.Join(" | ", launched)}");

        string untested = NewTree(scratch);
        Directory.Delete(Path.Combine(untested, "Tests/Application/A.Application.UnitTests"), recursive: true);

        var skipped = Invoke(untested, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(skipped.Code == 0 && skipped.Output.Contains("tests were skipped", StringComparison.Ordinal), "A tree without the application tests was not told its tests were skipped.");
        expect(!Directory.Exists(Path.Combine(untested, "Tests/Application/A.Application.UnitTests")), "Tests were written into a project that does not exist.");

        string undoubled = NewTree(scratch);
        File.Delete(Path.Combine(undoubled, "Tests/Application/A.Application.UnitTests/TestDoubles/ValidatedHandler.cs"));

        var halfTested = Invoke(undoubled, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(
            halfTested.Code == 0 && halfTested.Output.Contains("Not written: ApproveInvoiceTests.cs", StringComparison.Ordinal),
            "A tree without the ValidatedHandler double was not told the handler's test was left out.");
        expect(
            File.Exists(Path.Combine(undoubled, $"{tests}/Commands/ApproveInvoice/ApproveInvoiceCommandValidatorTests.cs"))
            && !File.Exists(Path.Combine(undoubled, $"{tests}/Commands/ApproveInvoice/ApproveInvoiceTests.cs")),
            "Without the ValidatedHandler double, the validator's tests were not written alone.");
    }

    private static void DtoCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string dto = Path.Combine(root, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceDto.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(dto)!);
        File.WriteAllText(dto, "public sealed record InvoiceDto(Guid Id);\n");

        var result = Invoke(root, "Invoices", "GetInvoice", "--kind", "query");
        string useCase = Path.Combine(root, "Src/Application/A.Application/Features/Invoices/UseCases/Queries/GetInvoice/GetInvoice.cs");

        expect(result.Code == 0, $"A query answering an existing DTO was refused: {result.Error}");
        expect(File.ReadAllText(dto) == "public sealed record InvoiceDto(Guid Id);\n", "The existing DTO was overwritten.");
        expect(File.Exists(useCase) && File.ReadAllText(useCase).Contains("Result<InvoiceDto>", StringComparison.Ordinal), "The query does not answer the existing DTO.");
        expect(result.Output.Contains("already declares", StringComparison.Ordinal), "The reuse of an existing DTO was not reported.");

        string fresh = NewTree(scratch);
        string freshDto = Path.Combine(fresh, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceTotalsDto.cs");

        expect(Invoke(fresh, "Invoices", "GetInvoiceTotals", "--kind", "query").Code == 0, "A query on a feature with a repository was refused.");
        expect(File.Exists(freshDto) && File.ReadAllText(freshDto).Contains("InvoiceTotalsDto(Guid Id);", StringComparison.Ordinal), "The generated DTO does not carry the aggregate's id.");

        string unrelated = Path.Combine(fresh, "Src/Application/A.Application/Features/Invoices/Model/Dtos/SummariseRevenueDto.cs");

        expect(Invoke(fresh, "Invoices", "SummariseRevenue", "--kind", "query", "--input", "none").Code == 0, "A query without input was refused.");
        expect(File.Exists(unrelated) && !File.ReadAllText(unrelated).Contains("Id", StringComparison.Ordinal), "A query without input answers a DTO carrying an id it was never given.");

        string bare = NewTree(scratch, "Unused");
        File.Delete(Path.Combine(bare, "Src/Domain/A.Domain/Features/Invoices/Repositories/IUnusedRepository.cs"));
        string bareDto = Path.Combine(bare, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceTotalsDto.cs");

        var bareResult = Invoke(bare, "Invoices", "GetInvoiceTotals", "--kind", "query", "--input", "none");

        expect(bareResult.Code == 0, $"A query on a feature without a repository was refused: {bareResult.Error}");
        expect(File.Exists(bareDto) && !File.ReadAllText(bareDto).Contains("Id", StringComparison.Ordinal), "The generated DTO invents an id for a feature with no aggregate.");
    }

    private static void ManifestCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string manifest = Path.Combine(root, FeatureManifest.Folder, "Invoices.json");
        const string generated = "Src/Application/A.Application/Features/Invoices/UseCases/Commands/CreateInvoice/CreateInvoice.cs";
        string page = $"docs/features/{CSharpNames.Kebab("Invoices")}.md";
        string original =
            "{\n  \"schemaVersion\": 1,\n  \"generator\": \"Tools/NewFeature.cs\",\n  \"feature\": \"Invoices\",\n  \"type\": \"Invoice\",\n"
            + "  \"options\": {\n    \"access\": \"shared\"\n  },\n"
            + $"  \"files\": [\n    {{\n      \"path\": \"{generated}\",\n      \"sha256\": \"{new string('a', 64)}\"\n    }}\n  ],\n"
            + $"  \"page\": \"{page}\"\n}}\n";

        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, original);

        var before = Snapshot(root);
        var first = Invoke(root, "Invoices", "ApproveInvoice", "--kind", "command");
        var second = Invoke(root, "Invoices", "GetInvoiceTotals", "--kind", "query");

        expect(first.Code == 0 && second.Code == 0, $"A use case was refused in a feature with a manifest: {first.Error}{second.Error}");
        expect(first.Output.Contains("remove-feature deletes them", StringComparison.Ordinal), "The manifest record was not reported.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var top = document.RootElement;
        var listed = top.GetProperty("files").EnumerateArray()
            .Select(file => (Path: file.GetProperty("path").GetString()!, Sha256: file.GetProperty("sha256").GetString()!))
            .ToList();

        var after = Snapshot(root);
        string[] created = [.. after.Keys.Except(before.Keys, StringComparer.Ordinal).Where(key => after[key] != "<directory>")];

        expect(created.Length == 10, $"Two use cases wrote {created.Length} files, not 9 and the response contract.");
        string[] outside =
        [
            _controller,
            _apiPage,
        ];

        expect(
            top.GetProperty("edits").EnumerateArray().Select(edit => edit.GetProperty("path").GetString()!).Distinct().Order(StringComparer.Ordinal).SequenceEqual(outside.Order(StringComparer.Ordinal)),
            $"The manifest does not record the edits made in files it does not list, the controller and the route table, and those alone: {string.Join(", ", top.GetProperty("edits").EnumerateArray().Select(edit => edit.GetProperty("path").GetString()))}");
        expect(top.GetProperty("schemaVersion").GetInt32() == 2, "A manifest that now records edits does not say schema version 2.");
        expect(listed.Select(entry => entry.Path).SequenceEqual(listed.Select(entry => entry.Path).Order(StringComparer.Ordinal)), "The manifest's files are not in path order.");
        expect(listed.Count == created.Length + 1 && listed.Any(entry => entry.Path == generated && entry.Sha256 == new string('a', 64)), "The manifest lost the generator's own entry or gained a stranger.");
        expect(!listed.Any(entry => entry.Path == _controller), "The manifest took in the controller, a file it did not list.");

        foreach (string key in created)
        {
            expect(listed.Any(entry => entry.Path == key && entry.Sha256 == after[key]), $"The manifest does not hold the hash of {key}.");
        }

        expect(
            top.GetProperty("generator").GetString() == "Tools/NewFeature.cs"
            && top.GetProperty("options").GetProperty("access").GetString() == "shared"
            && top.GetProperty("page").GetString() == page,
            "The manifest's other members were not kept.");
        expect(File.ReadAllText(manifest).EndsWith("}\n", StringComparison.Ordinal) && !File.ReadAllText(manifest).Contains('\r', StringComparison.Ordinal), "The manifest does not end in one LF.");
        expect(!File.Exists(manifest + ".new"), "The staged manifest was left behind.");

        string owned = NewTree(scratch);
        string ownedManifest = Path.Combine(owned, FeatureManifest.Folder, "Invoices.json");
        string controllerHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(owned, _controller))));

        Directory.CreateDirectory(Path.GetDirectoryName(ownedManifest)!);
        File.WriteAllText(ownedManifest, original.Replace($"\"path\": \"{generated}\",\n      \"sha256\": \"{new string('a', 64)}\"", $"\"path\": \"{_controller}\",\n      \"sha256\": \"{controllerHash}\"", StringComparison.Ordinal));

        expect(Invoke(owned, "Invoices", "ApproveInvoice", "--kind", "command").Code == 0, "A use case was refused in a feature whose manifest lists its controller.");

        using (var ownedDocument = JsonDocument.Parse(File.ReadAllText(ownedManifest)))
        {
            var ownedTop = ownedDocument.RootElement;
            string recorded = ownedTop.GetProperty("files").EnumerateArray().Single(file => file.GetProperty("path").GetString() == _controller).GetProperty("sha256").GetString()!;

            expect(
                recorded == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(owned, _controller)))),
                "The controller the feature owns, untouched until the action was added, is not recorded with its new hash, so remove-feature would take it for edited.");
            expect(
                !ownedTop.TryGetProperty("edits", out var ownedEdits)
                    || ownedEdits.EnumerateArray().All(edit => edit.GetProperty("path").GetString() != _controller),
                "The action added to a controller the feature owns is recorded as an edit of a file outside it.");
        }

        string broken = NewTree(scratch);
        string brokenManifest = Path.Combine(broken, FeatureManifest.Folder, "Invoices.json");

        Directory.CreateDirectory(Path.GetDirectoryName(brokenManifest)!);
        File.WriteAllText(brokenManifest, "{ not json");

        ExpectRefusal(broken, ["Invoices", "ApproveInvoice", "--kind", "command"], "cannot be read", expect);

        File.WriteAllText(brokenManifest, "{ \"feature\": \"Orders\", \"files\": [] }");

        ExpectRefusal(broken, ["Invoices", "ApproveInvoice", "--kind", "command"], "does not read as the manifest of Invoices", expect);
    }

    private static void WithoutTheApiCases(string scratch, Action<bool, string> expect)
    {
        string[][] cases =
        [
            ["ApproveInvoice", "--kind", "command"],
            ["GetInvoiceTotals", "--kind", "query"],
            ["FindInvoicesByCustomer", "--kind", "query", "--input", "id, Customer:string, Event:int"],
        ];

        foreach (string[] args in cases)
        {
            string root = NewTree(scratch);
            string label = string.Join(' ', args);

            Directory.Delete(Path.Combine(root, "Src/Presentation/A.Api"), recursive: true);
            Directory.CreateDirectory(Path.Combine(root, "Src/Presentation/A.Portal"));
            File.WriteAllText(Path.Combine(root, "Src/Presentation/A.Portal/Program.cs"), "builder.Services.AddInvoices();\n");

            var result = Invoke(root, ["Invoices", .. args]);

            expect(result.Code == 0, $"[{label}] without the API was refused: {result.Error}");
            expect(
                !Regex.IsMatch(result.Output, @"\bApi\b|api\.md|OpenAPI|openapi|HTTP|Controller", RegexOptions.None, TimeSpan.FromSeconds(2)),
                $"[{label}] without the API printed an instruction about it: {result.Output}");
            expect(
                result.Output.Contains("Src/Presentation/A.Portal/", StringComparison.Ordinal),
                $"[{label}] without the API did not name the host that serves the feature.");
            expect(!Directory.Exists(Path.Combine(root, "Src/Presentation/A.Api")), $"[{label}] without the API wrote into it.");

            string sent = args[0] switch
            {
                "ApproveInvoice" => "send new ApproveInvoiceCommand(invoiceId) through ISender",
                "GetInvoiceTotals" => "send new GetInvoiceTotalsQuery(invoiceId) through ISender",
                _ => "send new FindInvoicesByCustomerQuery(invoiceId, customer, @event) through ISender",
            };

            expect(result.Output.Contains(sent, StringComparison.Ordinal), $"[{label}] without the API did not print the message to send with its inputs: '{sent}'.");
        }
    }

    private static void RollbackCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        File.WriteAllText(Path.Combine(root, "Tests/Application/A.Application.UnitTests/Features"), "not a folder\n");

        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), "A write that fails half way was not reported as rolled back.");
        expect(Same(before, Snapshot(root)), "A write that failed half way left files or folders behind.");
    }

    private static IEnumerable<string> FileProblems(string root, string relative)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative));

        if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF)
        {
            yield return $"{relative}: no UTF-8 BOM, which the formatting gate refuses.";
        }

        string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (text.Contains('\r', StringComparison.Ordinal) || text.EndsWith("\n\n", StringComparison.Ordinal) || !text.EndsWith('\n'))
        {
            yield return $"{relative}: not LF endings closed by exactly one newline.";
        }

        if (UseCasePlan._tokenPattern.IsMatch(text))
        {
            yield return $"{relative}: a token was left unsubstituted: {UseCasePlan._tokenPattern.Match(text).Value}";
        }

        string code = relative.Contains("/Contracts/", StringComparison.Ordinal)
            ? string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("/// <summary>", StringComparison.Ordinal)))
            : text;

        if (code.Contains("//", StringComparison.Ordinal) || code.Contains("/*", StringComparison.Ordinal))
        {
            yield return $"{relative}: a comment, where generated code carries none outside an API contract's summary.";
        }

        string[] segments = relative.Split('/');
        int project = Array.FindIndex(segments, segment => segment.Contains('.', StringComparison.Ordinal));
        string prefix = GeneratorRun.Project(root).File.Name;
        segments[project] = prefix + "." + segments[project][FolderOf(prefix, string.Empty).Length..];

        if (!text.Contains($"namespace {string.Join('.', segments[project..^1])};", StringComparison.Ordinal))
        {
            yield return $"{relative}: its namespace is not its project plus its folders.";
        }

        string stem = Path.GetFileNameWithoutExtension(relative);
        string message = segments[^3] == "Commands" ? "Command" : "Query";
        string[] declared = segments[^2] == stem
            ? [$"{stem}{message}", $"{stem}{message}Handler"]
            : [stem];

        foreach (string type in declared.Where(type => !Regex.IsMatch(text, $@"^public (?:sealed )?(?:class|interface|record) {type}\b", RegexOptions.Multiline, TimeSpan.FromSeconds(2))))
        {
            yield return $"{relative}: does not declare the public '{type}' it is named for.";
        }

        string[] usings = [.. text.Split('\n').TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal))];

        if (usings.FirstOrDefault(line => !Regex.IsMatch(line, @"^using [A-Z][\w.]*;$", RegexOptions.None, TimeSpan.FromSeconds(1))) is { } malformed)
        {
            yield return $"{relative}: '{malformed}' names no namespace.";
        }

        if (!usings.SequenceEqual(usings.OrderBy(line => line.TrimEnd(';'), StringComparer.OrdinalIgnoreCase)))
        {
            yield return $"{relative}: its usings are not in order.";
        }
    }
}
