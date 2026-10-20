using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class EmailTemplateCoverageTests
{
    private const string _fallbackCulture = "en";

    private static readonly string[] _templateFolders =
    [
        "Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Accounts/Resources/Templates",
        //#if (reminders)
        "Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders",
        //#endif
    ];

    private static readonly Regex _templateName = new(
        @"^(?<mail>[A-Za-z]+EmailTemplate)\.(?<culture>[A-Za-z]{2}(?:-[A-Za-z]{2})?)\.html$",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _title = new(
        @"<title>(?<subject>.*?)</title>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void TheWalk_FindsEveryMailThisTemplateSends()
    {
        var families = Families();

        families.Keys.Order(StringComparer.Ordinal).ToList().ShouldBe(
            [
                "AlreadyRegisteredEmailTemplate",
                "EmailChangeEmailTemplate",
                "PasswordResetEmailTemplate",
                "RegisterEmailTemplate",
                //#if (reminders)
                "ReminderEmailTemplate",
                //#endif
            ],
            customMessage:
            "These are the mails this template sends. A mail that stopped being found here is "
            + "one no rule below is checking any more.");
    }

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
