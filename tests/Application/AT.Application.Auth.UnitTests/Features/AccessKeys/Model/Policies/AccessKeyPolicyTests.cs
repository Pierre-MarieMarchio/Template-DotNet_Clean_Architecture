using System.Buffers.Text;
using System.Security.Cryptography;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.AccessKeys.Model.Policies;

public sealed class AccessKeyPolicyTests
{
    [Fact]
    public void TheBounds_AreThoseTheReferenceDocuments()
    {
        AccessKeyPolicy.NameMaxLength.ShouldBe(100);
        AccessKeyPolicy.MaxActiveKeysPerAccount.ShouldBe(25);
        AccessKeyPolicy.LastUseResolution.ShouldBe(TimeSpan.FromMinutes(1));
        AccessKeyPolicy.Scopes.ShouldBe(["read", "write"]);
    }

    [Fact]
    public void AKeyOfThePrefixAndThirtyTwoRandomBytes_IsWellFormed() =>
        AccessKeyPolicy.IsWellFormed(AKey()).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ak_")]
    [InlineData("ak_not*base64url*at*all*not*base64url*at*all")]
    public void AnythingElse_IsNot(string? key) => AccessKeyPolicy.IsWellFormed(key).ShouldBeFalse();

    [Fact]
    public void AKeyWithoutThePrefix_IsNot() =>
        AccessKeyPolicy.IsWellFormed(AKey()[AccessKeyPolicy.KeyPrefix.Length..]).ShouldBeFalse();

    [Fact]
    public void AKeyOfTheWrongSize_IsNot() =>
        AccessKeyPolicy.IsWellFormed(
            AccessKeyPolicy.KeyPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(31)))
            .ShouldBeFalse();

    private static string AKey() =>
        AccessKeyPolicy.KeyPrefix
        + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(AccessKeyPolicy.SecretSizeInBytes));
}
