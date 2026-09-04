using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

public sealed class ConfirmFileUploadCommandValidator : AbstractValidator<ConfirmFileUploadCommand>
{
    public ConfirmFileUploadCommandValidator() =>
        RuleFor(command => command.StoredFileId)
            .NotEmpty().WithMessage("A file id is required.");
}
