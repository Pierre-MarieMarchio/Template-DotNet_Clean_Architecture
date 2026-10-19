using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.ValueObjects;

public sealed class FileCategoryTests
{
    [Theory]
    [InlineData("document")]
    [InlineData("profile-photo")]
    [InlineData("2026-invoices")]
    [InlineData("a")]
    public void Create_KeepsAWellFormedSlug(string value) =>
        FileCategory.Create(value).Value.ShouldBe(value);

    [Fact]
    public void Create_AcceptsTheLongestName() =>
        FileCategory.Create(new string('a', FileCategory.MaxLength)).Value.Length.ShouldBe(FileCategory.MaxLength);

    /// <summary>One spelling per category: casing and padding are refused rather than normalised.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Document")]
    [InlineData(" document")]
    [InlineData("profile_photo")]
    [InlineData("profile photo")]
    [InlineData("files/2026")]
    [InlineData("café")]
    public void Create_Rejects_AnythingButALowerCaseSlug(string value) =>
        Should.Throw<DomainException>(() => FileCategory.Create(value));

    [Fact]
    public void Create_Rejects_ANullValue() =>
        Should.Throw<DomainException>(() => FileCategory.Create(null!));

    [Fact]
    public void Create_Rejects_ANameOneCharacterTooLong() =>
        Should.Throw<DomainException>(() => FileCategory.Create(new string('a', FileCategory.MaxLength + 1)));

    [Fact]
    public void IsValid_AgreesWithCreate()
    {
        FileCategory.IsValid("document").ShouldBeTrue();
        FileCategory.IsValid("Document").ShouldBeFalse();
        FileCategory.IsValid(null).ShouldBeFalse();
    }

    [Fact]
    public void Equality_IsByValue() =>
        FileCategory.Create("document").ShouldBe(FileCategory.Create("document"));
}
