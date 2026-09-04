using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

public sealed class IssueFileDownloadQueryValidator : AbstractValidator<IssueFileDownloadQuery>
{
    public IssueFileDownloadQueryValidator() =>
        RuleFor(query => query.StoredFileId)
            .NotEmpty().WithMessage("A file id is required.");
}
