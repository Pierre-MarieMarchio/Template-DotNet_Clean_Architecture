using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// That a measurement a host takes is a measurement somebody can read.
/// <para>
/// An instrument is recorded to whether or not anything listens, and OpenTelemetry exports only the
/// meters and sources a host names explicitly. So a <c>Meter</c> declared and never passed to
/// <c>AddMeter</c> costs the same to record, reads correctly in a unit test holding a
/// <c>MeterListener</c>, and reaches no collector. Nothing about the declaration says whether the
/// registration exists, and prose beside it is not evidence — which is why this is a rule rather
/// than a convention.
/// </para>
/// <para>
/// Read from the source tree, for the reason <c>BackgroundWorkTests</c> gives: this project
/// deliberately does not reference the host, so the host is invisible to reflection from here and
/// the text is what is left. The hosts themselves come from the disk, so a second one is covered the
/// day it exists.
/// </para>
/// <para>
/// Only a host's <em>own</em> instruments are in scope. A meter belonging to a module a host
/// composes is that host's decision to export or not — the API names such a meter beside the job
/// that records on it, since nothing else would make it observable.
/// </para>
/// </summary>
public sealed class ObservabilityRegistrationTests
{
    /// <summary>
    /// The name constant a diagnostics class exposes so that the declaration and the registration can
    /// be one string. Requiring the constant is half the rule: a name written as a literal at the
    /// point of construction is a name this rule cannot follow, and the check below reports a host
    /// file that declares an instrument without one.
    /// </summary>
    private static readonly Regex _nameConstant = new(
        @"const\s+string\s+Name\s*=\s*""([^""]+)""\s*;",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A field of one of the two instrument types. Matches the target-typed form these classes
    /// actually use — <c>static readonly Meter _meter = new(Name);</c> — where the type appears only
    /// on the left of the assignment.
    /// </summary>
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

            // Somewhere this rule reads has to exist wherever there is an instrument to register.
            // Without one, no registration text is read and every instrument the host declares is
            // reported with no place named to fix it in — a failure that reads as the host's and is
            // the rule's. The count below cannot catch it, so it is asserted here.
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

    /// <summary>
    /// Proves the matcher can say no, which the rule above cannot: it reports offenders, so a
    /// matcher that answered "registered" to everything would read as a pass. Both spellings the
    /// rule accepts are checked, because accepting only the literal would quietly fail every
    /// registration in this repository — all of which go through the constant.
    /// </summary>
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

    /// <summary>
    /// Where a host names what it exports: every <c>Common/Observability/</c> file, which is the
    /// host's own registration, and every file called <c>*Telemetry.cs</c>, which is how a feature
    /// that owns instruments adds them to that pipeline — Common may not name a feature, so the jobs'
    /// registration sits beside the jobs. Concatenated. A host with neither registers nothing, which
    /// is an empty string and therefore an offender for each instrument it declares — the correct
    /// answer rather than a skip.
    /// </summary>
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
