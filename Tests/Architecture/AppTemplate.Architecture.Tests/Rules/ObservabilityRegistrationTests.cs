using System.Text.RegularExpressions;
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

            bool declaresAnything = SourceFilesUnder(directory).Any(file =>
            {
                string text = File.ReadAllText(file);

                return _meterField.IsMatch(text) || _activitySourceField.IsMatch(text);
            });

            if (declaresAnything && registrations.Length == 0)
            {
                offenders.Add(
                    $"{host.Name}: declares an instrument and has no 'Common/Observability/' or " +
                    "'*Telemetry.cs' to register it in. A host keeps a thin extension of its own over " +
                    "the shared one, and that is where its own instruments are named; folding the " +
                    "call straight into Program.cs leaves this rule nothing to read.");
                continue;
            }

            foreach (string file in SourceFilesUnder(directory))
            {
                string text = File.ReadAllText(file);
                bool declaresMeter = _meterField.IsMatch(text);
                bool declaresSource = _activitySourceField.IsMatch(text);

                if (!declaresMeter && !declaresSource)
                {
                    continue;
                }

                string owner = Path.GetFileNameWithoutExtension(file);
                var name = _nameConstant.Match(text);

                if (!name.Success)
                {
                    offenders.Add(
                        $"{host.Name}: '{owner}' declares an instrument whose name is not a " +
                        "'const string Name', so nothing can hold the declaration and the " +
                        "registration to one string.");
                    continue;
                }

                if (declaresMeter)
                {
                    checkedInstruments++;

                    if (!IsRegistered(registrations, "AddMeter", owner, name.Groups[1].Value))
                    {
                        offenders.Add(
                            $"{host.Name}: '{owner}' declares a Meter named " +
                            $"'{name.Groups[1].Value}' that no AddMeter call names.");
                    }
                }

                if (declaresSource)
                {
                    checkedInstruments++;

                    if (!IsRegistered(registrations, "AddSource", owner, name.Groups[1].Value))
                    {
                        offenders.Add(
                            $"{host.Name}: '{owner}' declares an ActivitySource named " +
                            $"'{name.Groups[1].Value}' that no AddSource call names.");
                    }
                }
            }
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
}
