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
        "Src/Infrastructure/AT.Infrastructure.Auth/Features/Accounts/Resources/Templates",
        //#if (reminders)
        "Src/Infrastructure/AT.Infrastructure.Email/Features/Reminders",
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
        LanguageMismatches(families).ShouldBeEmpty(
            "A language one mail can be written in and another cannot is a deployment that answers "
            + "its users in two languages. Add the missing template, named "
            + "<Mail>EmailTemplate.<culture>.html, in the same folder as its siblings, or remove the "
            + "language everywhere.");
    }

    [Fact]
    public void EveryMail_ShipsTheFallbackLanguage()
    {
        WithoutTheFallback(Families()).ShouldBeEmpty(
            $"'{_fallbackCulture}' is what a reader with no matching template receives, so a mail "
            + $"without it has readers it cannot be written for at all. Add <Mail>EmailTemplate.{_fallbackCulture}.html "
            + "for each mail named here.");
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
            "A mail's subject is its template's <title>. Without one there is no subject to send. " +
            "Give each template named here a <title> holding the subject in its own language.");
    }

    [Fact]
    public void NoMail_UsesOneSubjectForTwoLanguages()
    {
        SharedSubjects(Templates().Select(file => (file, Subject(File.ReadAllText(file.Path))))).ShouldBeEmpty(
            "One subject serving two languages means a template was translated in the body and not "
            + "in the title — which is the half of the mail a reader sees first. Translate the "
            + "<title> of each template named here.");
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

    private static List<string> LanguageMismatches(Dictionary<string, HashSet<string>> families)
    {
        var expected = families.Values.SelectMany(cultures => cultures).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. families
                .Where(family => !expected.All(culture => family.Value.Contains(culture, StringComparer.Ordinal)))
                .Select(family =>
                    $"{family.Key} has [{string.Join(", ", family.Value.Order(StringComparer.Ordinal))}], "
                    + $"and the mails together have [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]")
                .Order(StringComparer.Ordinal),
        ];
    }

    private static List<string> WithoutTheFallback(Dictionary<string, HashSet<string>> families) =>
        [.. families
            .Where(family => !family.Value.Contains(_fallbackCulture, StringComparer.Ordinal))
            .Select(family => family.Key)
            .Order(StringComparer.Ordinal)];

    private static List<string> SharedSubjects(IEnumerable<(TemplateFile File, string Subject)> templates)
    {
        var offenders = new List<string>();

        foreach (var family in templates.GroupBy(template => template.File.Mail, StringComparer.Ordinal))
        {
            var bySubject = family
                .GroupBy(template => template.Subject, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            offenders.AddRange(bySubject.Select(group =>
                $"{family.Key}: '{group.Key}' is the subject of "
                + string.Join(" and ", group.Select(template => template.File.Culture).Order(StringComparer.Ordinal))));
        }

        return [.. offenders.Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void TheCoverageRules_AreSensitive_AndSeeAMissingLanguageAMissingFallbackAndASharedSubject()
    {
        var families = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["RegisterEmailTemplate"] = ["en", "fr"],
            ["ResetEmailTemplate"] = ["en"],
            ["ReminderEmailTemplate"] = ["fr"],
        };

        LanguageMismatches(families).ShouldBe(
            [
                "ReminderEmailTemplate has [fr], and the mails together have [en, fr]",
                "ResetEmailTemplate has [en], and the mails together have [en, fr]",
            ]);
        WithoutTheFallback(families).ShouldBe(["ReminderEmailTemplate"]);

        var english = new TemplateFile("RegisterEmailTemplate", "en", "RegisterEmailTemplate.en.html", "a");
        var french = new TemplateFile("RegisterEmailTemplate", "fr", "RegisterEmailTemplate.fr.html", "b");

        SharedSubjects([(english, "Welcome"), (french, "welcome")])
            .ShouldBe(["RegisterEmailTemplate: 'Welcome' is the subject of en and fr"]);
        SharedSubjects([(english, "Welcome"), (french, "Bienvenue")]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("RegisterEmailTemplate.en.html", true)]
    [InlineData("RegisterEmailTemplate.pt-BR.html", true)]
    [InlineData("RegisterEmailTemplate.html", false)]
    [InlineData("Register.en.html", false)]
    [InlineData("RegisterEmailTemplate.eng.html", false)]
    public void TheFileName_IsSensitive_AndMatchesOnlyAMailAndACulture(string name, bool matches) =>
        _templateName.IsMatch(name).ShouldBe(matches);

    [Theory]
    [InlineData("<html><title>Welcome</title></html>", true, "Welcome")]
    [InlineData("<title>  </title>", false, "")]
    [InlineData("<html></html>", false, "")]
    public void TheTitleRule_IsSensitive_AndNeedsANonBlankTitle(string content, bool has, string subject)
    {
        HasTitle(content).ShouldBe(has);
        Subject(content).ShouldBe(subject);
    }

    private static bool HasTitle(string content)
    {
        var match = _title.Match(content);

        return match.Success && !string.IsNullOrWhiteSpace(match.Groups["subject"].Value);
    }

    private static string Subject(string content) => _title.Match(content).Groups["subject"].Value.Trim();

    private sealed record TemplateFile(string Mail, string Culture, string Name, string Path);
}
