using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

public sealed class DeleteStoredFileCommandValidator : AbstractValidator<DeleteStoredFileCommand>
{
    public DeleteStoredFileCommandValidator() =>
        RuleFor(command => command.StoredFileId)
            .NotEmpty().WithMessage("A file id is required.");
}
