using AppTemplate.Infrastructure.Identity.Features.Auth.Factories;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Factories;

/// <summary>
/// Which language a mail comes out in, and where its subject comes from. Exercised through the
/// tag-taking overload rather than the ambient one, so nothing here depends on what the process
/// happens to have set.
/// </summary>
public sealed class EmailBodyFactoryTests
{
    private static readonly Dictionary<string, string> _placeholders =
        new(StringComparer.Ordinal) { ["UserName"] = "Ada", ["ConfirmationLink"] = "https://localhost/x" };

    private readonly EmailBodyFactory _sut = new("RegisterEmailTemplate");

    [Fact]
    public void TheAvailableLanguages_AreTheOnesWithATemplate() =>
        _sut.AvailableCultures.Order(StringComparer.Ordinal).ShouldBe(["en", "fr"]);

    [Theory]
    [InlineData("en", "Confirm your email address")]
    [InlineData("fr", "Confirmez votre adresse e-mail")]
    public void EachLanguage_BringsItsOwnSubject(string tag, string subject) =>
        _sut.Create(tag, _placeholders).Subject.ShouldBe(
            subject,
            "the subject is the template's <title>, so it follows the body");

    [Fact]
    public void TheSubjectAndTheBody_AreAlwaysInTheSameLanguage()
    {
        var french = _sut.Create("fr", _placeholders);

        french.Subject.ShouldBe("Confirmez votre adresse e-mail");
        french.Body.ShouldContain("Bienvenue");
        french.Body.ShouldNotContain(
            "Thank you for signing up",
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
        _sut.Create(tag, _placeholders).Subject.ShouldBe("Confirmez votre adresse e-mail");

    [Theory]
    [InlineData("de")]
    [InlineData("ja-JP")]
    public void ALanguageWithNoTemplate_FallsBackToEnglish(string tag) =>
        _sut.Create(tag, _placeholders).Subject.ShouldBe("Confirm your email address");

    /// <summary>
    /// A tag that is not a tag at all — an <c>Accept-Language</c> of <c>*</c>, a truncated header,
    /// nothing — is not an error. It is a reader whose language is unknown.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("not a tag")]
    public void AMalformedTag_FallsBackToEnglish(string tag) =>
        _sut.Create(tag, _placeholders).Subject.ShouldBe("Confirm your email address");

    [Fact]
    public void EveryPlaceholder_IsHtmlEncodedInTheBody()
    {
        var mail = _sut.Create(
            "fr",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = "<script>alert(1)</script>",
                ["ConfirmationLink"] = "https://localhost/x",
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
        var mail = _sut.Create(
            "en",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["UserName"] = "Ada\r\nBcc: victim@example.test",
                ["ConfirmationLink"] = "https://localhost/x",
            });

        mail.Subject.ShouldBe("Confirm your email address");
        mail.Subject.ShouldNotContain("Bcc");
    }

    [Fact]
    public void AMailWithNoTemplateAtAll_FailsLoudly()
    {
        var missing = new EmailBodyFactory("NoSuchEmailTemplate");

        var exception = Should.Throw<InvalidOperationException>(() => missing.Create("en", _placeholders));

        exception.Message.ShouldContain("NoSuchEmailTemplate");
    }

}
