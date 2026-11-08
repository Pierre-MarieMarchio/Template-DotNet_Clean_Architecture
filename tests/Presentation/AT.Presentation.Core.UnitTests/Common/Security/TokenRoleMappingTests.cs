using System.Text;
using System.Text.Json;
using AppTemplate.Presentation.Core.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Security;

public sealed class TokenRoleMappingTests
{
    [Fact]
    public void ATopLevelArray_GivesEachOfItsStrings() =>
        RolesAt("""{"roles":["Admin","Reader"]}""", "roles").ShouldBe(["Admin", "Reader"]);

    [Fact]
    public void ANestedArray_IsReachedByItsDotPath() =>
        RolesAt("""{"realm_access":{"roles":["Admin"]}}""", "realm_access.roles").ShouldBe(["Admin"]);

    [Fact]
    public void ADeeplyNestedArray_IsReachedThroughEveryObject() =>
        RolesAt("""{"resource_access":{"api":{"roles":["Writer"]}}}""", "resource_access.api.roles")
            .ShouldBe(["Writer"]);

    [Fact]
    public void ASingleString_IsOneRole() =>
        RolesAt("""{"role":"Admin"}""", "role").ShouldBe(["Admin"]);

    [Fact]
    public void ANamespacedClaimWithDotsInItsName_IsMatchedWhole() =>
        RolesAt("""{"https://example.com/roles":["Admin"]}""", "https://example.com/roles").ShouldBe(["Admin"]);

    [Fact]
    public void AMissingClaim_IsNoRole() =>
        RolesAt("""{"sub":"abc"}""", "realm_access.roles").ShouldBeEmpty();

    [Fact]
    public void APathThroughANonObject_IsNoRole() =>
        RolesAt("""{"realm_access":["roles"]}""", "realm_access.roles").ShouldBeEmpty();

    [Fact]
    public void AnObjectAtThePath_IsNoRole() =>
        RolesAt("""{"roles":{"Admin":true}}""", "roles").ShouldBeEmpty();

    [Fact]
    public void NonStringsBlanksAndDuplicates_AreDropped() =>
        RolesAt("""{"roles":["Admin",1,null,"","  ",{"x":1},"Admin","Reader"]}""", "roles")
            .ShouldBe(["Admin", "Reader"]);

    [Fact]
    public void ATokenWithNoPayload_GivesNoRole() =>
        TokenRoleMapping.Read(encodedPayload: null, "roles").ShouldBeEmpty();

    [Fact]
    public void AnEncodedPayload_IsReadAsTheTokenSentIt() =>
        TokenRoleMapping.Read(
                Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"realm_access":{"roles":["Admin"]}}"""))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                "realm_access.roles")
            .ShouldBe(["Admin"]);

    private static IReadOnlyList<string> RolesAt(string json, string path)
    {
        using var document = JsonDocument.Parse(json);

        return TokenRoleMapping.RolesAt(document.RootElement, path);
    }
}
