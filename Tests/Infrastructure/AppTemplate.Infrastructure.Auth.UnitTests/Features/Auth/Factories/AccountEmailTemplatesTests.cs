using System.Reflection;
using AppTemplate.Infrastructure.Auth.Features.Auth.Factories;
using AppTemplate.Infrastructure.Core.Common.Templating;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Auth.Factories;

/// <summary>
/// What this module's three account mails actually say, in each language it ships. The rendering
/// mechanics belong to <see cref="EmailTemplate"/> and are exercised in its own mirror; what is
/// asserted here is that these templates carry a subject in every language, and that the subject
/// and the body never come from different ones.
/// </summary>
public sealed class AccountEmailTemplatesTests
{
    private static readonly Assembly _module = typeof(ConfirmationEmailFactory).Assembly;

    private static readonly Dictionary<string, string> _placeholders = new(StringComparer.Ordinal)
    {
        ["UserName"] = "Ada",
        ["ConfirmationLink"] = "https://localhost/x",
        ["ResetLink"] = "https://localhost/x",
    };

    [Theory]
    [InlineData("RegisterEmailTemplate", "en", "Confirm your email address")]
    [InlineData("RegisterEmailTemplate", "fr", "Confirmez votre adresse e-mail")]
    [InlineData("PasswordResetEmailTemplate", "en", "Reset your password")]
    [InlineData("PasswordResetEmailTemplate", "fr", "Réinitialisez votre mot de passe")]
    [InlineData("EmailChangeEmailTemplate", "en", "Confirm your new email address")]
    [InlineData("EmailChangeEmailTemplate", "fr", "Confirmez votre nouvelle adresse e-mail")]
    public void EachMail_BringsItsOwnSubject_InEachLanguage(string baseName, string tag, string subject) =>
        new EmailTemplate(_module, baseName).Render(tag, _placeholders).Subject.ShouldBe(
            subject,
            "the subject is the template's <title>, so it follows the body");

    [Fact]
    public void TheSubjectAndTheBody_AreAlwaysInTheSameLanguage()
    {
        var french = new EmailTemplate(_module, "RegisterEmailTemplate").Render("fr", _placeholders);

        french.Subject.ShouldBe("Confirmez votre adresse e-mail");
        french.Body.ShouldContain("Bienvenue");
        french.Body.ShouldNotContain(
            "Thank you for signing up",
            Case.Sensitive,
            "a French subject over an English body is the defect this arrangement exists to prevent");
    }

    [Theory]
    [InlineData("RegisterEmailTemplate")]
    [InlineData("PasswordResetEmailTemplate")]
    [InlineData("EmailChangeEmailTemplate")]
    public void EveryMail_ShipsTheSameLanguages(string baseName) =>
        new EmailTemplate(_module, baseName).AvailableCultures
            .Order(StringComparer.Ordinal)
            .ShouldBe(["en", "fr"]);
}
