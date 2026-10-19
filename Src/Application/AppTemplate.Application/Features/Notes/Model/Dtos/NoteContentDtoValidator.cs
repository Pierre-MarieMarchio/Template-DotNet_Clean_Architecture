using AppTemplate.Application.Features.Notes.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Features.Notes.Model.Dtos;

public sealed class NoteContentDtoValidator : AbstractValidator<NoteContentDto>
{
    public NoteContentDtoValidator()
    {
        RuleFor(content => content.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A note title is required.")
            // Measured after trimming, because that is what is stored.
            .Must(title => title.Trim().Length <= NoteInputPolicy.TitleMaxLength)
            .WithMessage($"A note title cannot exceed {NoteInputPolicy.TitleMaxLength} characters.");

        // An empty body is a note with a title only; a missing one is a malformed request.
        RuleFor(content => content.Body)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A note body is required; it may be empty.")
            .MaximumLength(NoteInputPolicy.BodyMaxLength)
            .WithMessage($"A note body cannot exceed {NoteInputPolicy.BodyMaxLength} characters.");
    }
}
