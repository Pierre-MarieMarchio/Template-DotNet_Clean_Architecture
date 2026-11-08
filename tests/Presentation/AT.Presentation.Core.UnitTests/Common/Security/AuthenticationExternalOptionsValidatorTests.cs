using AppTemplate.Presentation.Core.Common.Security;
using Shouldly;
using Xunit;
using RoleSource = AppTemplate.Presentation.Core.Common.Security.AuthenticationExternalOptions.RoleSource;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Security;

public sealed class AuthenticationExternalOptionsValidatorTests
{
    private static readonly AuthenticationExternalOptionsValidator _validator = new();

    [Fact]
    public void AnAuthorityAndAnAudience_AreEnough_WithRolesReadLocally()
    {
        var options = Valid();

        options.Roles.Source.ShouldBe(RoleSource.Local);
        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void TheClaimDefaults_AreTheOpenIdConnectStandardOnes()
    {
        var options = new AuthenticationExternalOptions();

        options.SubjectClaim.ShouldBe("sub");
        options.EmailClaim.ShouldBe("email");
        options.NameClaim.ShouldBe("name");
        options.RequireHttpsMetadata.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/realms/app")]
    [InlineData("ftp://idp.example.com")]
    public void AnAuthorityThatIsNotAnAbsoluteHttpUrl_IsRefused(string authority) =>
        Failures(With(options => options.Authority = authority)).ShouldContain(failure => failure.Contains("Authority"));

    [Fact]
    public void AnHttpAuthority_IsRefused_WhileHttpsMetadataIsRequired() =>
        Failures(With(options => options.Authority = "http://localhost:8080/realms/app"))
            .ShouldContain(failure => failure.Contains("RequireHttpsMetadata"));

    [Fact]
    public void AnHttpAuthority_IsAccepted_OnceHttpsMetadataIsWaived()
    {
        var options = Valid();
        options.Authority = "http://localhost:8080/realms/app";
        options.RequireHttpsMetadata = false;

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void AMissingAudience_IsRefused() =>
        Failures(With(options => options.Audience = " ")).ShouldContain(failure => failure.Contains("Audience"));

    [Theory]
    [InlineData(nameof(AuthenticationExternalOptions.SubjectClaim))]
    [InlineData(nameof(AuthenticationExternalOptions.EmailClaim))]
    [InlineData(nameof(AuthenticationExternalOptions.NameClaim))]
    public void ABlankClaimName_IsRefused(string key)
    {
        var options = Valid();
        typeof(AuthenticationExternalOptions).GetProperty(key)!.SetValue(options, "");

        Failures(options).ShouldContain(failure => failure.Contains(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".roles")]
    [InlineData("realm_access.")]
    [InlineData("realm_access..roles")]
    public void TokenRolesWithoutAUsableClaimPath_AreRefused(string? path)
    {
        var options = Valid();
        options.Roles.Source = RoleSource.Claim;
        options.Roles.ClaimPath = path;

        Failures(options).ShouldContain(failure => failure.Contains("ClaimPath"));
    }

    [Fact]
    public void TokenRolesWithAClaimPath_AreAccepted()
    {
        var options = Valid();
        options.Roles.Source = RoleSource.Claim;
        options.Roles.ClaimPath = "realm_access.roles";

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void AnUndefinedRoleSource_IsRefused()
    {
        var options = Valid();
        options.Roles.Source = (RoleSource)0;

        Failures(options).ShouldContain(failure => failure.Contains("Source"));
    }

    [Fact]
    public void ABlankAdministratorRole_IsRefused()
    {
        var options = Valid();
        options.Roles.AdministratorRole = "";

        Failures(options).ShouldContain(failure => failure.Contains("AdministratorRole"));
    }

    [Fact]
    public void EveryFailure_IsReportedAtOnce()
    {
        var options = new AuthenticationExternalOptions();

        Failures(options).Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    private static AuthenticationExternalOptions Valid() => new()
    {
        Authority = "https://idp.example.com/realms/app",
        Audience = "app-api",
    };

    private static AuthenticationExternalOptions With(Action<AuthenticationExternalOptions> change)
    {
        var options = Valid();
        change(options);

        return options;
    }

    private static IReadOnlyList<string> Failures(AuthenticationExternalOptions options)
    {
        var result = _validator.Validate(null, options);
        result.Failed.ShouldBeTrue();

        return [.. result.Failures!];
    }
}
