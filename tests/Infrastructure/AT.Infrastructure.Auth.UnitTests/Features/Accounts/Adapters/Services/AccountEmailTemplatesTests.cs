using System.Reflection;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Core.Common.Templating;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

public sealed class AccountEmailTemplatesTests
{
    private static readonly Assembly _module = typeof(AccountEmailFactory).Assembly;

    private static readonly Dictionary<string, string> _placeholders = new(StringComparer.Ordinal)
    {
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
    [InlineData("AlreadyRegisteredEmailTemplate", "en", "You already have an account")]
    [InlineData("AlreadyRegisteredEmailTemplate", "fr", "Vous avez déjà un compte")]
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
    [InlineData("AlreadyRegisteredEmailTemplate")]
    public void EveryMail_ShipsTheSameLanguages(string baseName) =>
        new EmailTemplate(_module, baseName).AvailableCultures
            .Order(StringComparer.Ordinal)
            .ShouldBe(["en", "fr"]);
}
