using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// A mail is a subject and a body, and the failure worth guarding against is the two disagreeing
/// about their language — an English subject over a French body is wrong in a way neither half is
/// wrong on its own, so nothing but a rule about the pair can see it.
/// <para>
/// A mail's subject is its body's own <c>&lt;title&gt;</c>, which is what keeps that pair together.
/// What no single file can keep together is the two modules:
/// <c>AppTemplate.Infrastructure.Identity</c> renders the three account mails and
/// <c>AppTemplate.Infrastructure.Email</c> the reminder, they may not reference each other, and a
/// language added to one folder and not the other gives a deployment a password reset in French and
/// a reminder in English. These rules are what stands between that and nobody noticing.
/// </para>
/// <para>
/// Read from the source tree rather than from the assemblies: what ships is what is on disk, and a
/// template MSBuild silently declined to embed — which is what a missing
/// <c>WithCulture="false"</c> does — would leave a metadata-driven rule reporting nothing at all.
/// </para>
/// </summary>
public sealed class EmailTemplateCoverageTests
{
    /// <summary>
    /// Every mail must be writable in it, because it is what a reader whose own language has no
    /// template receives. Both renderers name the same constant.
    /// </summary>
    private const string _fallbackCulture = "en";

    /// <summary>
    /// The folders holding mail templates, and the module each belongs to. Two entries because two
    /// modules send mail; a third module that started sending some would be added here, and the
    /// count below is what refuses a silent removal.
    /// </summary>
    private static readonly string[] _templateFolders =
    [
        "Src/Infrastructure/AppTemplate.Infrastructure.Identity/Features/Auth/Templates",
        "Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders",
    ];

    private static readonly Regex _templateName = new(
        @"^(?<mail>[A-Za-z]+EmailTemplate)\.(?<culture>[A-Za-z]{2}(?:-[A-Za-z]{2})?)\.html$",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _title = new(
        @"<title>(?<subject>.*?)</title>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Proves the walk found the templates at all. Without it every rule below would pass on an
    /// empty set — which is precisely what a renamed folder would produce.
    /// </summary>
    [Fact]
    public void TheWalk_FindsEveryMailThisTemplateSends()
    {
        var families = Families();

        families.Keys.Order(StringComparer.Ordinal).ToList().ShouldBe(
            [
                "EmailChangeEmailTemplate",
                "PasswordResetEmailTemplate",
                "RegisterEmailTemplate",
                "ReminderEmailTemplate",
            ],
            customMessage:
            "These are the four mails this template sends. A mail that stopped being found here is "
            + "one no rule below is checking any more.");
    }

    /// <summary>
    /// The rule that matters. A language ships or it does not; shipping it for the account mails and
    /// not for the reminder is worse than not shipping it, because the gap only shows up when a
    /// reminder finally comes due.
    /// </summary>
    [Fact]
    public void EveryMail_ShipsTheSameLanguagesAsEveryOther()
    {
        var families = Families();
        var expected = families.Values.SelectMany(cultures => cultures).ToHashSet(StringComparer.Ordinal);

        var mismatched = families
            .Where(family => !expected.All(culture => family.Value.Contains(culture, StringComparer.Ordinal)))
            .Select(family =>
                $"{family.Key} has [{string.Join(", ", family.Value.Order(StringComparer.Ordinal))}], "
                + $"and the mails together have [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]")
            .Order(StringComparer.Ordinal)
            .ToList();

        mismatched.ShouldBeEmpty(
            "A language one mail can be written in and another cannot is a deployment that answers "
            + "its users in two languages. Add the missing template, or remove the language "
            + "everywhere.");
    }

    [Fact]
    public void EveryMail_ShipsTheFallbackLanguage()
    {
        var without = Families()
            .Where(family => !family.Value.Contains(_fallbackCulture, StringComparer.Ordinal))
            .Select(family => family.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        without.ShouldBeEmpty(
            $"'{_fallbackCulture}' is what a reader with no matching template receives, so a mail "
            + "without it has readers it cannot be written for at all.");
    }

    /// <summary>
    /// The subject. A template with no title renders a mail with none, and both renderers throw
    /// rather than send one — at the first send, in production. Here instead.
    /// </summary>
    [Fact]
    public void EveryTemplate_CarriesANonEmptySubjectInItsTitle()
    {
        var offenders = Templates()
            .Where(file => !HasTitle(File.ReadAllText(file.Path)))
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A mail's subject is its template's <title>. Without one there is no subject to send.");
    }

    /// <summary>
    /// Two templates of the same mail in different languages must not carry the same subject: it is
    /// the sign of a file copied and translated in the body only, which is the exact defect that
    /// started this — and it is invisible in a diff of the body.
    /// </summary>
    [Fact]
    public void NoMail_UsesOneSubjectForTwoLanguages()
    {
        var offenders = new List<string>();

        foreach (var family in Templates().GroupBy(file => file.Mail, StringComparer.Ordinal))
        {
            var bySubject = family
                .GroupBy(file => Subject(File.ReadAllText(file.Path)), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            offenders.AddRange(bySubject.Select(group =>
                $"{family.Key}: '{group.Key}' is the subject of "
                + string.Join(" and ", group.Select(file => file.Culture).Order(StringComparer.Ordinal))));
        }

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "One subject serving two languages means a template was translated in the body and not "
            + "in the title — which is the half of the mail a reader sees first.");
    }

    private static Dictionary<string, HashSet<string>> Families()
    {
        var families = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var template in Templates())
        {
            if (!families.TryGetValue(template.Mail, out var cultures))
            {
                cultures = new HashSet<string>(StringComparer.Ordinal);
                families[template.Mail] = cultures;
            }

            cultures.Add(template.Culture);
        }

        return families;
    }

    private static List<TemplateFile> Templates()
    {
        var found = new List<TemplateFile>();

        foreach (string folder in _templateFolders)
        {
            string path = Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder);

            Directory.Exists(path).ShouldBeTrue(
                $"'{folder}' does not exist, so every rule in this class would judge an empty set.");

            foreach (string file in Directory.EnumerateFiles(path, "*.html"))
            {
                string name = Path.GetFileName(file);
                var parsed = _templateName.Match(name);

                parsed.Success.ShouldBeTrue(
                    $"'{folder}/{name}' is not named <Mail>EmailTemplate.<culture>.html. Both "
                    + "renderers read the culture out of that name, so one that does not match is a "
                    + "file nothing will ever send.");

                found.Add(new TemplateFile(
                    parsed.Groups["mail"].Value,
                    parsed.Groups["culture"].Value,
                    name,
                    file));
            }
        }

        return found;
    }

    private static bool HasTitle(string content)
    {
        var match = _title.Match(content);

        return match.Success && !string.IsNullOrWhiteSpace(match.Groups["subject"].Value);
    }

    private static string Subject(string content) => _title.Match(content).Groups["subject"].Value.Trim();

    private sealed record TemplateFile(string Mail, string Culture, string Name, string Path);
}
