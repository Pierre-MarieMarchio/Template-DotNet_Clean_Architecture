using System.Text.RegularExpressions;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ObservabilityRegistrationTests
{
    private static readonly Regex _nameConstant = new(
        @"const\s+string\s+Name\s*=\s*""([^""]+)""\s*;",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _meterField = new(
        @"\bMeter\s+[A-Za-z_][A-Za-z0-9_]*\s*=",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _activitySourceField = new(
        @"\bActivitySource\s+[A-Za-z_][A-Za-z0-9_]*\s*=",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _observabilityCall = new(
        @"\.Add(Api)?Observability\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryDiagnosticsNameAHostDeclares_IsRegisteredByThatHost()
    {
        int checkedInstruments = 0;
        var offenders = new List<string>();

        foreach (var host in ProjectReferenceGraph.Hosts)
        {
            string directory = Path.Combine(
                ProjectReferenceGraph.RepositoryRoot,
                Path.GetDirectoryName(host.RelativePath)!);

            string registrations = ReadObservabilitySource(directory);
            var instruments = InstrumentsOf(directory);

            if (instruments.Count > 0 && registrations.Length == 0)
            {
                offenders.Add(
                    $"{host.Name}: declares an instrument and has no 'Common/Observability/' or " +
                    "'*Telemetry.cs' to register it in. A host keeps a thin extension of its own over " +
                    "the shared one, and that is where its own instruments are named; folding the " +
                    "call straight into Program.cs leaves this rule nothing to read.");
                continue;
            }

            checkedInstruments += instruments.Count(instrument => instrument.Name is not null);
            offenders.AddRange(RegistrationProblems(host.Name, instruments, registrations));
        }

        checkedInstruments.ShouldBeGreaterThanOrEqualTo(
            2,
            "Far fewer instruments were found across the hosts than this template declares, so the " +
            "walk is not reading the tree it is meant to describe and every one of them would read " +
            "as registered.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "An instrument is recorded whether or not it is exported, so this costs the same as " +
            "working and reports nothing. Name it in the host's Common/Observability/ registration or " +
            "in the *Telemetry.cs beside the feature that owns it — " +
            "AddMeter for a Meter, AddSource for an ActivitySource.");
    }

    [Fact]
    public void TheUseCaseInstruments_AreRegisteredByEveryHostsObservability()
    {
        string registration = File.ReadAllText(Path.Combine(
            ProjectReferenceGraph.RootOf(ProjectReferenceGraph.SourceProjects.Values.Single(project => project.Name == "AppTemplate.Presentation.Core")),
            "Common",
            "Observability",
            "ObservabilityExtensions.cs"));

        IsRegistered(registration, "AddSource", nameof(UseCaseInstruments), UseCaseInstruments.Name).ShouldBeTrue(
            "Every use case opens an activity under UseCaseInstruments; the shared registration every host " +
            "calls must export it, or the traces stop at the controller.");

        IsRegistered(registration, "AddMeter", nameof(UseCaseInstruments), UseCaseInstruments.Name).ShouldBeTrue(
            "Every use case records its duration on UseCaseInstruments' meter; the shared registration " +
            "every host calls must export it.");
    }

    [Fact]
    public void EveryHost_ComposesTheSharedObservability_SoItsLogsTracesAndMetricsLeaveTogether()
    {
        var hosts = ProjectReferenceGraph.Hosts.ToList();

        hosts.ShouldNotBeEmpty();

        var offenders = hosts
            .Where(host => !_observabilityCall.IsMatch(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RootOf(host), "Program.cs"))))
            .Select(host => host.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "The shared observability registers the OTLP exporters for logs, traces and metrics behind one " +
            "setting; a host that does not call it, directly or through its own thin extension, exports none " +
            "of them. These hosts' Program.cs call neither 'AddObservability' nor 'AddApiObservability'.");
    }

    [Fact]
    public void TheRegistrationMatcher_IsSensitive_AndTellsTheTwoSpellingsFromSilence()
    {
        const string name = "AppTemplate.Probe";

        IsRegistered(".AddMeter(ProbeDiagnostics.Name)", "AddMeter", "ProbeDiagnostics", name)
            .ShouldBeTrue("The constant is how every registration in this repository is written.");

        IsRegistered($".AddMeter(\"{name}\")", "AddMeter", "ProbeDiagnostics", name)
            .ShouldBeTrue("A literal naming the same meter registers it just as well.");

        IsRegistered(".AddMeter(OtherDiagnostics.Name)", "AddMeter", "ProbeDiagnostics", name)
            .ShouldBeFalse("Another class's constant must not count as this one's registration.");

        IsRegistered(".AddSource(ProbeDiagnostics.Name)", "AddMeter", "ProbeDiagnostics", name)
            .ShouldBeFalse("A span source is not a meter; the two calls are not interchangeable.");

        IsRegistered("/// see AddMeter and ProbeDiagnostics.Name", "AddMeter", "ProbeDiagnostics", name)
            .ShouldBeFalse("Prose that mentions the call is not the call.");
    }

    private static bool IsRegistered(string source, string call, string owner, string name) =>
        Regex.IsMatch(
            source,
            $@"{Regex.Escape(call)}\(\s*(?:{Regex.Escape(owner)}\.Name|""{Regex.Escape(name)}"")\s*\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

    private static List<Instrument> InstrumentsOf(string hostDirectory) =>
        [.. SourceFilesUnder(hostDirectory).SelectMany(InstrumentsIn)];

    private static IEnumerable<Instrument> InstrumentsIn(string file)
    {
        string text = File.ReadAllText(file);
        string owner = Path.GetFileNameWithoutExtension(file);
        var name = _nameConstant.Match(text);
        string? value = name.Success ? name.Groups[1].Value : null;

        if (_meterField.IsMatch(text))
        {
            yield return new Instrument(owner, value, "a Meter", "AddMeter");
        }

        if (_activitySourceField.IsMatch(text))
        {
            yield return new Instrument(owner, value, "an ActivitySource", "AddSource");
        }
    }

    private static IEnumerable<string> RegistrationProblems(
        string host,
        IReadOnlyCollection<Instrument> instruments,
        string registrations)
    {
        var unnamed = instruments
            .Where(instrument => instrument.Name is null)
            .Select(instrument => instrument.Owner)
            .Distinct(StringComparer.Ordinal)
            .Select(owner =>
                $"{host}: '{owner}' declares an instrument whose name is not a " +
                "'const string Name', so nothing can hold the declaration and the " +
                "registration to one string.");

        var unregistered = instruments
            .Where(instrument => instrument.Name is { } name && !IsRegistered(registrations, instrument.Call, instrument.Owner, name))
            .Select(instrument =>
                $"{host}: '{instrument.Owner}' declares {instrument.Kind} named " +
                $"'{instrument.Name}' that no {instrument.Call} call names.");

        return unnamed.Concat(unregistered);
    }

    private static string ReadObservabilitySource(string hostDirectory)
    {
        string folder = Path.Combine(hostDirectory, "Common", "Observability");

        IEnumerable<string> registrations = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.cs")
            : [];

        var features = SourceFilesUnder(hostDirectory)
            .Where(file => file.EndsWith("Telemetry.cs", StringComparison.Ordinal));

        return string.Join(
            '\n',
            registrations.Concat(features).Distinct(StringComparer.Ordinal).Select(File.ReadAllText));
    }

    private static IEnumerable<string> SourceFilesUnder(string hostDirectory) =>
        Directory
            .EnumerateFiles(hostDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private sealed record Instrument(string Owner, string? Name, string Kind, string Call);
}
