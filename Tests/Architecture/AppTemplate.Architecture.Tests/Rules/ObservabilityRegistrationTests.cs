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
/// <c>MeterListener</c>, and reaches no collector — which is precisely what happened to the Files
/// loop: four counters and an <c>ActivitySource</c>, produced on every pass of the loop that decides
/// whether a deposited file ever becomes readable, and thrown away. Its own documentation claimed
/// the registration existed.
/// </para>
/// <para>
/// Read from the source tree, for the reason <c>BackgroundWorkTests</c> gives: this project
/// deliberately does not reference either host, so the hosts are invisible to reflection from here
/// and the text is what is left. The hosts themselves come from the disk, so a third one is covered
/// the day it exists.
/// </para>
/// <para>
/// Only a host's <em>own</em> instruments are in scope. A meter belonging to a module a host
/// composes is that host's decision to export or not — the worker names
/// <c>"AppTemplate.Reminders"</c> from the persistence module and the API does not, correctly, since
/// reminders fire in one of the two.
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
        var checkedInstruments = 0;
        var offenders = new List<string>();

        foreach (var host in ProjectReferenceGraph.Hosts)
        {
            string directory = Path.Combine(
                ProjectReferenceGraph.RepositoryRoot,
                Path.GetDirectoryName(host.RelativePath)!);

            string registrations = ReadObservabilitySource(directory);

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
            6,
            "Far fewer instruments were found across the hosts than this template declares, so the " +
            "walk is not reading the tree it is meant to describe and every one of them would read " +
            "as registered.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "An instrument is recorded whether or not it is exported, so this costs the same as " +
            "working and reports nothing. Name it in the host's Common/Observability/ registration — " +
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
        const string name = "AppTemplate.Worker.Probe";

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
    /// Every <c>Common/Observability/</c> file of one host, concatenated. A host that has no such
    /// folder registers nothing, which is an empty string and therefore an offender for each
    /// instrument it declares — the correct answer rather than a skip.
    /// </summary>
    private static string ReadObservabilitySource(string hostDirectory)
    {
        string folder = Path.Combine(hostDirectory, "Common", "Observability");

        return Directory.Exists(folder)
            ? string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Select(File.ReadAllText))
            : string.Empty;
    }

    private static IEnumerable<string> SourceFilesUnder(string hostDirectory) =>
        Directory
            .EnumerateFiles(hostDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
}
