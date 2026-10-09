using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Policies;
using AppTemplate.Application.Features.Notes.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Notes.Validators;

public sealed class NoteContentDtoValidatorTests
{
    private static readonly NoteContentDtoValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATitleAndABody_AreAccepted() =>
        (await _validator.ValidateAsync(new NoteContentDto("Groceries", "Milk"), TestToken)).IsValid.ShouldBeTrue();

    [Fact]
    public async Task AnEmptyBody_IsAccepted() =>
        (await _validator.ValidateAsync(new NoteContentDto("Groceries", string.Empty), TestToken)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankTitle_IsRejected(string title) =>
        (await _validator.ValidateAsync(new NoteContentDto(title, "Milk"), TestToken)).IsValid.ShouldBeFalse();

    /// <summary>
    /// A null reaches the validator from a body that is entirely well formed JSON, and the rules that
    /// dereference the value must not turn it into a 500.
    /// </summary>
    [Fact]
    public async Task ANullTitle_IsRejected_NotDereferenced() =>
        (await _validator.ValidateAsync(new NoteContentDto(null!, "Milk"), TestToken)).IsValid.ShouldBeFalse();

    [Fact]
    public async Task ANullBody_IsRejected() =>
        (await _validator.ValidateAsync(new NoteContentDto("Groceries", null!), TestToken)).IsValid.ShouldBeFalse();

    /// <summary>The title is measured after trimming, because that is what is stored.</summary>
    [Fact]
    public async Task AMaximumLengthTitle_PaddedWithWhitespace_IsAccepted() =>
        (await _validator.ValidateAsync(
            new NoteContentDto($"  {new string('a', NoteInputPolicy.TitleMaxLength)}  ", "Milk"),
            TestToken)).IsValid.ShouldBeTrue();

    [Fact]
    public async Task ATitleOneCharacterTooLong_IsRejected() =>
        (await _validator.ValidateAsync(
            new NoteContentDto(new string('a', NoteInputPolicy.TitleMaxLength + 1), "Milk"),
            TestToken)).IsValid.ShouldBeFalse();

    [Fact]
    public async Task AMaximumLengthBody_IsAccepted() =>
        (await _validator.ValidateAsync(
            new NoteContentDto("Groceries", new string('a', NoteInputPolicy.BodyMaxLength)),
            TestToken)).IsValid.ShouldBeTrue();

    [Fact]
    public async Task ABodyOneCharacterTooLong_IsRejected() =>
        (await _validator.ValidateAsync(
            new NoteContentDto("Groceries", new string('a', NoteInputPolicy.BodyMaxLength + 1)),
            TestToken)).IsValid.ShouldBeFalse();
}
