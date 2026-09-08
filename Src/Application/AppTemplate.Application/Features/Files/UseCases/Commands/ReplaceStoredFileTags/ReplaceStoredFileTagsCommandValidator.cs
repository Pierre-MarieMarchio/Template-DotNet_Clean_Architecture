using AppTemplate.Application.Common.Tagging;
using AppTemplate.Domain.Features.Files.Entities;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;

public sealed class ReplaceStoredFileTagsCommandValidator : AbstractValidator<ReplaceStoredFileTagsCommand>
{
    public ReplaceStoredFileTagsCommandValidator()
    {
        RuleFor(command => command.StoredFileId)
            .NotEmpty().WithMessage("A file id is required.");
        RuleFor(command => command.Tags)
            .IsATagSet(StoredFile.MaxTags, "stored file");
        RuleForEach(command => command.Tags)
            .IsATag();
    }
}
