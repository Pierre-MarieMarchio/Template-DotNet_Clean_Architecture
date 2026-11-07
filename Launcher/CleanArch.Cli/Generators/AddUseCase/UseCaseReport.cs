using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddUseCase;

internal sealed class UseCaseReport
{
    private readonly TextWriter _output;

    private readonly string _root;

    private readonly UseCaseSpec _spec;

    private readonly List<Emission> _plan;

    private readonly ReportMode _mode;

    private readonly string _validatorTests;

    private readonly bool _testsWritten;

    private readonly bool _handlerTested;

    private readonly string _api;

    private readonly string _page;

    private readonly bool _hasApi;

    private UseCaseReport(TextWriter output, string root, UseCaseSpec spec, List<Emission> plan, ReportMode mode)
    {
        _output = output;
        _root = root;
        _spec = spec;
        _plan = plan;
        _mode = mode;
        _validatorTests = $"{spec.Input}ValidatorTests";
        _testsWritten = plan.Any(emission => emission.Path.EndsWith($"/{_validatorTests}.cs", StringComparison.Ordinal));
        _handlerTested = plan.Any(emission => emission.Path.EndsWith($"/{spec.Name}Tests.cs", StringComparison.Ordinal));
        _api = $"{Presentation}/{FolderOf(spec.Prefix, "Api")}";
        _page = $"docs/features/{CSharpNames.Kebab(spec.Features)}.md";
        _hasApi = Directory.Exists(Path.Combine(root, _api));
    }

    private bool HasPage => File.Exists(Path.Combine(_root, _page));

    internal static void Write(TextWriter output, string root, UseCaseSpec spec, List<Emission> plan, string? skipped, ReportMode mode)
    {
        var report = new UseCaseReport(output, root, spec, plan, mode);

        report.Files(skipped);
        report.Handler();
        report.UnknownTypes();
        report.YoursToWrite();

        if (report._hasApi)
        {
            report.ServedOverHttp();
        }
        else
        {
            report.OfferedByHand();
        }
    }

    private void Files(string? skipped)
    {
        _output.WriteLine($"{(_mode == ReportMode.Applied ? "Wrote" : "Would write")} {_plan.Count} files for the {_spec.Kind} {_spec.Name} in {_spec.Features}:");

        foreach (var emission in _plan)
        {
            _output.WriteLine($"  {emission.Path}");
        }

        _output.WriteLine();

        if (_spec.DtoExists)
        {
            _output.WriteLine($"It answers the {_spec.Dto} the feature already declares under Model/Dtos.");
        }

        _output.WriteLine(_spec.Manifest is null
            ? $"No manifest names {_spec.Features}, so remove feature never deletes these files; they are the feature's like any it holds."
            : $"Recorded in {_spec.Manifest} with their SHA-256, the contracts among them, and with each edit made outside the feature's files, so remove feature deletes them and undoes those edits with the feature.");

        if (skipped is not null)
        {
            _output.WriteLine($"Not written: '{skipped}' is not in this tree, so the use case's tests were skipped.");
        }
        else if (_testsWritten && !_handlerTested)
        {
            _output.WriteLine($"Not written: {_spec.Name}Tests.cs, whose UseCase() runs the handler behind its validator through TestDoubles/ValidatedHandler.cs, which this tree's application tests do not hold.");
        }
    }

    private void Handler()
    {
        _output.WriteLine();
        _output.WriteLine($"Nothing to register: AddFeature(\"{_spec.Features}\") in ApplicationModule finds its validator by folder, and each host's generated mediator registers its handler.");
        _output.WriteLine(_spec.HasValidator
            ? $"Until its body is written, {_spec.Input}Handler throws NotImplementedException; the pipeline answers what its validator refuses before the handler is reached."
            : $"Until its body is written, {_spec.Input}Handler throws NotImplementedException.");

        if (!_spec.HasValidator && _spec.Parameters.Count > 0)
        {
            _output.WriteLine(
                "No validator and no test were written: no input is one this command writes a rule for, a bool or a nullable accepting every value. "
                + $"Write {_spec.Input}Validator beside {_spec.Name}.cs once a value is refused, with {_validatorTests} and {_spec.Name}Tests beside the feature's other tests.");
        }
    }

    private void UnknownTypes()
    {
        if (_spec.Unknown.Length == 0)
        {
            return;
        }

        _output.WriteLine();
        _output.WriteLine("Written as given, of a type this command knows no rule and no test value for:");

        foreach (var parameter in _spec.Unknown)
        {
            _output.WriteLine($"  {parameter.Name}  {parameter.Type}");
        }

        _output.WriteLine("For each, yours to write:");
        _output.WriteLine(_spec.HasValidator
            ? $"  - its rule in {_spec.Input}Validator, when a value of it is refused, and that refusal's test in {_validatorTests}"
            : "  - its rule, when a value of it is refused, in the validator above");

        if (_testsWritten)
        {
            _output.WriteLine($"  - a real value in AWellFormed{(_spec.IsCommand ? "Command" : "Query")}() in {_validatorTests}, which passes default! for it until then");
        }

        _output.WriteLine(_hasApi && _spec.HasRequest
            ? $"  - a using of its namespace in {_spec.Name}.cs and in {_spec.Request}.cs, unless the type is imported already"
            : $"  - a using of its namespace in {_spec.Name}.cs, unless the type is imported already");
    }

    private void YoursToWrite()
    {
        _output.WriteLine();
        _output.WriteLine("What it decides is yours to write, each with its test (docs/guides/testing.md):");

        if (_spec.IsCommand)
        {
            _output.WriteLine($"  - the rule, as a method of the {_spec.Aggregate ?? "aggregate"} in the domain, and a domain test per invariant");
            _output.WriteLine("  - the body: load through the feature's service or repository, call that method, commit through IUnitOfWork");
        }
        else
        {
            _output.WriteLine($"  - {_spec.Dto}'s members, and a method of the feature's queries port that projects them");
            _output.WriteLine("  - the body: ask ICurrentUser for the caller when the feature is owned, then that method");
        }

        _output.WriteLine(_handlerTested
            ? $"  - its dependencies, in the handler's primary constructor; in {_spec.Name}Tests, each one a substitute field passed where UseCase() builds the handler, the one place that does"
            : "  - its dependencies, in the handler's primary constructor");
        _output.WriteLine("  - one unit test per decision the body adds, each refusal with its control");

        if (_hasApi)
        {
            _output.WriteLine("  - an error code of its own: its row in docs/reference/api.md, which the error-code tests read");
        }
    }

    private void OfferedByHand()
    {
        _output.WriteLine();
        _output.WriteLine($"To offer it, send {UseCaseHttp.Construction(_spec)} through ISender by hand where a host serves {_spec.Features}, under:");

        foreach (string host in UseCaseHttp.Hosts(_root))
        {
            _output.WriteLine($"  Src/Presentation/{host}/");
        }

        if (HasPage)
        {
            _output.WriteLine($"and add the operation to {_page}.");
        }
    }

    private void ServedOverHttp()
    {
        Action();
        Contracts();
        ContractSteps();
        RouteRow();
    }

    private void Action()
    {
        string controller = $"{_api}/Features/{_spec.Features}/Controllers/{_spec.Features}Controller.cs";
        string where = File.Exists(Path.Combine(_root, controller))
            ? controller
            : $"a controller under {_api}/Features/{_spec.Features}/Controllers/";

        _output.WriteLine();
        _output.WriteLine(_mode switch
        {
            ReportMode.Applied => $"To serve it over HTTP, the edit made in {where}:",
            ReportMode.DryRun => $"To serve it over HTTP, the edit a run without --dry-run makes in {where}:",
            _ => $"To serve it over HTTP, the edit by hand in {where}:",
        });

        string[] usings = UseCaseHttp.ControllerUsings(_spec);

        _output.WriteLine($"  the usings it lacks  {usings[0]};");

        foreach (string space in usings.Skip(1))
        {
            _output.WriteLine($"                       {space};");
        }

        _output.WriteLine("  the parameter        ISender sender, in the primary constructor, unless it has one");
        _output.WriteLine("  the action");
        Indented(UseCaseHttp.ActionLines(_spec));
    }

    private void Contracts()
    {
        if (_spec.HasRequest)
        {
            _output.WriteLine($"  and {UseCaseHttp.RequestPath(_spec)}, with what a client sends:");
            Indented(UseCaseHttp.RequestContract(_spec));
        }

        if (!_spec.IsCommand)
        {
            _output.WriteLine($"  and {UseCaseHttp.ResponsePath(_spec)}, with the members a client reads:");
            Indented(UseCaseHttp.ResponseContract(_spec));
        }
    }

    private void Indented(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            _output.WriteLine(line.Length == 0 ? string.Empty : "      " + line);
        }
    }

    private void ContractSteps()
    {
        if (_mode == ReportMode.Applied)
        {
            return;
        }

        _output.WriteLine();
        _output.WriteLine(_mode == ReportMode.DryRun
            ? "Then a run without --dry-run rewrites what the contract feeds, in this order:"
            : "Then rewrite what the contract feeds, once the edit is made:");

        foreach (var step in TextEdits.ContractSteps(_root, built: false))
        {
            _output.WriteLine($"  {step.Display}");
        }
    }

    private void RouteRow()
    {
        string written = _spec.Routing?.Documented.StartsWith("/api/", StringComparison.Ordinal) == true
            ? "written in full, from /api"
            : "relative to that table's 'Under' prefix";

        _output.WriteLine(_mode == ReportMode.Applied
            ? $"and its row, added to the table of {_spec.Features} under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads, {written}:"
            : $"and its row in the table of {_spec.Features} under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads, {written}:");
        _output.WriteLine("  " + UseCaseHttp.RouteRowText(_spec));

        if (HasPage)
        {
            _output.WriteLine($"and add the route to {_page}.");
        }
    }
}
