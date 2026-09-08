using System.Reflection;
using AppTemplate.Infrastructure.Core.Common.Templating;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Templating;

/// <summary>
/// Which language a mail comes out in, where its subject comes from, and what happens to a
/// substituted value. Exercised against this project's own embedded templates, so nothing here
/// depends on what any module happens to ship.
/// </summary>
public sealed class EmailTemplateTests
{
    private static readonly Assembly _resources = typeof(EmailTemplateTests).Assembly;

    private static readonly Dictionary<string, string> _placeholders =
        new(StringComparer.Ordinal) { ["UserName"] = "Ada" };

    private readonly EmailTemplate _sut = new(_resources, "TestEmailTemplate");

    [Fact]
    public void TheAvailableLanguages_AreTheOnesWithATemplate() =>
        _sut.AvailableCultures.Order(StringComparer.Ordinal).ShouldBe(["en", "fr"]);

    [Theory]
    [InlineData("en", "Test subject")]
    [InlineData("fr", "Sujet de test")]
    public void EachLanguage_BringsItsOwnSubject(string tag, string subject) =>
        _sut.Render(tag, _placeholders).Subject.ShouldBe(
            subject,
            "the subject is the template's <title>, so it follows the body");

    [Fact]
    public void TheSubjectAndTheBody_AreAlwaysInTheSameLanguage()
    {
        var french = _sut.Render("fr", _placeholders);

        french.Subject.ShouldBe("Sujet de test");
        french.Body.ShouldContain("corps français");
        french.Body.ShouldNotContain(
            "English body",
            Case.Sensitive,
            "a French subject over an English body is the defect this arrangement exists to prevent");
    }

    /// <summary>
    /// A regional tag reaches its language's template rather than the fallback: a reader whose
    /// browser says <c>fr-CA</c> is a French reader, and one template per region is not something
    /// this template asks of anybody.
    /// </summary>
    [Theory]
    [InlineData("fr-CA")]
    [InlineData("fr-BE")]
    public void ARegionalTag_FallsBackToItsParentLanguage(string tag) =>
        _sut.Render(tag, _placeholders).Subject.ShouldBe("Sujet de test");

    [Theory]
    [InlineData("de")]
    [InlineData("ja-JP")]
    public void ALanguageWithNoTemplate_FallsBackToEnglish(string tag) =>
        _sut.Render(tag, _placeholders).Subject.ShouldBe("Test subject");

    /// <summary>
    /// A tag that is not a tag at all — an <c>Accept-Language</c> of <c>*</c>, a truncated header,
    /// nothing — is not an error. It is a reader whose language is unknown.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("not a tag")]
    public void AMalformedTag_FallsBackToEnglish(string tag) =>
        _sut.Render(tag, _placeholders).Subject.ShouldBe("Test subject");

    [Fact]
    public void EveryPlaceholder_IsHtmlEncodedInTheBody()
    {
        var mail = _sut.Render(
            "en",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = "<script>alert(1)</script>",
            });

        mail.Body.ShouldNotContain("<script>");
        mail.Body.ShouldContain("&lt;script&gt;");
    }

    /// <summary>
    /// Substitution touches the body alone. A mail header is not HTML, so the body's encoding would
    /// be the wrong one there, and an unencoded value carrying a newline would be header injection.
    /// </summary>
    [Fact]
    public void TheSubject_TakesNoSubstitution()
    {
        var mail = _sut.Render(
            "en",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = "Ada\r\nBcc: victim@example.test",
            });

        mail.Subject.ShouldBe("Test subject");
        mail.Subject.ShouldNotContain("Bcc");
    }

    [Fact]
    public void AMailWithNoTemplateAtAll_FailsLoudly()
    {
        var missing = new EmailTemplate(_resources, "NoSuchEmailTemplate");

        var exception = Should.Throw<InvalidOperationException>(() => missing.Render("en", _placeholders));

        exception.Message.ShouldContain("NoSuchEmailTemplate");
        exception.Message.ShouldContain(_resources.GetName().Name!);
    }

    /// <summary>
    /// The assembly is a parameter so that a module renders its own mail: reading this class's own
    /// assembly would make every module's templates unreachable from here.
    /// </summary>
    [Fact]
    public void TheTemplatesAreReadFromTheAssemblyItIsGiven()
    {
        var elsewhere = new EmailTemplate(typeof(EmailTemplate).Assembly, "TestEmailTemplate");

        Should.Throw<InvalidOperationException>(() => elsewhere.Render("en", _placeholders));
    }

    [Fact]
    public void ItRefusesAMissingAssemblyOrName()
    {
        Should.Throw<ArgumentNullException>(() => new EmailTemplate(null!, "TestEmailTemplate"));
        Should.Throw<ArgumentException>(() => new EmailTemplate(_resources, "  "));
    }
}
