using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;

public sealed class GetStoredFileQueryValidator : AbstractValidator<GetStoredFileQuery>
{
    public GetStoredFileQueryValidator() =>
        RuleFor(query => query.StoredFileId)
            .NotEmpty().WithMessage("A file id is required.");
}
